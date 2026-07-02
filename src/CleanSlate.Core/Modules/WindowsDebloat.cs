using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Modules;

public enum DebloatCategory { Telemetrie, Confidentialite, Interface, Applications }

/// <summary>Valeur de registre sauvegardée avant modification (pour pouvoir la restaurer).</summary>
public sealed record RegistryValueBackup(string KeyPath, string Name, string? Kind, string? Value, bool Existed);

/// <summary>État de démarrage d'un service sauvegardé avant sa désactivation.</summary>
public sealed record ServiceStartBackup(string ServiceName, int? PreviousStart, bool Existed);

/// <summary>
/// Sauvegarde complète d'une session de debloat, permettant de tout restaurer :
/// valeurs de registre d'origine, états de démarrage des services, et tâches désactivées.
/// (Le retrait d'applications préinstallées n'est PAS réversible — réinstallation depuis le Store.)
/// </summary>
public sealed class DebloatBackup
{
    public List<RegistryValueBackup> Registry { get; set; } = new();
    public List<ServiceStartBackup> Services { get; set; } = new();
    public List<string> DisabledTasks { get; set; } = new();
}

/// <summary>Une option de « debloat » que l'utilisateur peut cocher avant exécution.</summary>
public sealed record DebloatOption(
    string Id,
    string Name,
    string Description,
    DebloatCategory Category,
    bool RecommendedDefault);

/// <summary>Résultat d'une application de debloat.</summary>
public sealed record DebloatResult(int Applied, int Failed, IReadOnlyList<string> Messages);

/// <summary>
/// Module « Windows Debloat » (sous-catégorie de l'Optimisation).
///
/// Désactive la télémétrie Microsoft, renforce la confidentialité, allège l'interface
/// et retire les applications préinstallées inutiles (bloatware). Chaque action est
/// EXPLICITEMENT choisie par l'utilisateur avant exécution.
///
/// ⚠️ Les modifications touchent le registre, des services et des applications UWP.
/// Elles sont standard et documentées, mais nécessitent les droits administrateur.
/// Les applications retirées restent réinstallables depuis le Microsoft Store.
/// </summary>
public interface IWindowsDebloater
{
    IReadOnlyList<DebloatOption> GetOptions();
    Task<DebloatResult> ApplyAsync(IEnumerable<string> optionIds, IProgress<string>? progress, CancellationToken ct);

    /// <summary>Vrai si une sauvegarde de debloat existe et peut être restaurée.</summary>
    bool HasBackup { get; }

    /// <summary>
    /// Restaure l'état d'origine sauvegardé lors des applications précédentes (valeurs
    /// de registre, démarrage des services, tâches planifiées). N'annule PAS le retrait
    /// d'applications préinstallées (réinstallables depuis le Microsoft Store).
    /// </summary>
    Task<DebloatResult> RevertAsync(IProgress<string>? progress, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDebloatService : IWindowsDebloater
{
    private readonly IActionLogger _logger;
    private readonly string _backupFile;

    // Sentinelle pour distinguer « valeur absente » de « valeur nulle » à la lecture du registre.
    private static readonly object AbsentSentinel = new();

    // Sauvegarde en cours de constitution pendant une passe d'application.
    private DebloatBackup? _capture;

    public WindowsDebloatService(IActionLogger logger, string? backupFile = null)
    {
        _logger = logger;
        _backupFile = backupFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "debloat-backup.json");
    }

    public bool HasBackup => File.Exists(_backupFile);

    private static readonly IReadOnlyList<DebloatOption> Options = new[]
    {
        new DebloatOption("telemetry",
            "Désactiver la télémétrie Microsoft",
            "Coupe la collecte de données (AllowTelemetry=0) et désactive les services DiagTrack et dmwappushservice.",
            DebloatCategory.Telemetrie, true),

        new DebloatOption("telemetry-tasks",
            "Désactiver les tâches planifiées de télémétrie",
            "Désactive les tâches du Programme d'amélioration (CEIP) et de compatibilité qui envoient des données.",
            DebloatCategory.Telemetrie, true),

        new DebloatOption("advertising-id",
            "Désactiver l'identifiant de publicité",
            "Empêche les applications d'utiliser votre ID de publicité pour vous cibler.",
            DebloatCategory.Confidentialite, true),

        new DebloatOption("activity-history",
            "Désactiver l'historique d'activité (Timeline)",
            "Windows n'enregistre plus et n'envoie plus votre historique d'activité.",
            DebloatCategory.Confidentialite, true),

        new DebloatOption("location",
            "Désactiver le suivi de localisation",
            "Refuse l'accès à la localisation au niveau système.",
            DebloatCategory.Confidentialite, false),

        new DebloatOption("cortana",
            "Désactiver Cortana",
            "Désactive l'assistant Cortana via une stratégie système.",
            DebloatCategory.Interface, true),

        new DebloatOption("web-search",
            "Désactiver la recherche Web dans le menu Démarrer",
            "Le menu Démarrer ne renvoie plus de résultats Bing/Web — recherche locale uniquement.",
            DebloatCategory.Interface, true),

        new DebloatOption("tips",
            "Désactiver les suggestions, pubs et « contenus proposés »",
            "Coupe les suggestions d'applications, les astuces et les contenus promotionnels de Windows.",
            DebloatCategory.Interface, true),

        new DebloatOption("background-apps",
            "Désactiver les applications en arrière-plan",
            "Empêche les applications UWP de s'exécuter en arrière-plan (gain de RAM/CPU).",
            DebloatCategory.Interface, false),

        new DebloatOption("bloatware",
            "Retirer les applications préinstallées inutiles",
            "Désinstalle un lot d'applications (météo, actualités, solitaire, Clipchamp, Candy Crush, etc.). " +
            "Réinstallables depuis le Store.",
            DebloatCategory.Applications, true),
    };

    public IReadOnlyList<DebloatOption> GetOptions() => Options;

    public Task<DebloatResult> ApplyAsync(IEnumerable<string> optionIds, IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var wanted = optionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int applied = 0, failed = 0;
            var messages = new List<string>();

            // On repart de la sauvegarde existante (accumulation) afin de conserver la
            // toute première valeur d'origine, même après plusieurs applications.
            _capture = LoadBackup() ?? new DebloatBackup();
            try
            {
                foreach (var opt in Options.Where(o => wanted.Contains(o.Id)))
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Application : {opt.Name}…");
                    try
                    {
                        RunOption(opt.Id, messages, ct);
                        applied++;
                        messages.Add($"✅ {opt.Name}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        messages.Add($"⚠️ {opt.Name} : {ex.Message}");
                        _logger.Error($"Debloat « {opt.Id} » échoué", ex);
                    }
                }
            }
            finally
            {
                SaveBackup(_capture);
                _capture = null;
            }

            _logger.Info($"Debloat terminé : {applied} appliqué(s), {failed} échec(s).");
            return new DebloatResult(applied, failed, messages);
        }, ct);
    }

    // =====================================================================
    //  Implémentation des options
    // =====================================================================
    private void RunOption(string id, List<string> messages, CancellationToken ct)
    {
        switch (id)
        {
            case "telemetry":
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0);
                DisableService("DiagTrack");
                DisableService("dmwappushservice");
                break;

            case "telemetry-tasks":
                DisableTasks(messages,
                    @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                    @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                    @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                    @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                    @"\Microsoft\Windows\Autochk\Proxy");
                break;

            case "advertising-id":
                SetDword(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1);
                break;

            case "activity-history":
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
                break;

            case "location":
                SetString(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                    "Value", "Deny");
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1);
                break;

            case "cortana":
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
                break;

            case "web-search":
                SetDword(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                SetDword(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
                break;

            case "tips":
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1);
                const string cdm = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                SetDword(cdm, "SilentInstalledAppsEnabled", 0);
                SetDword(cdm, "SystemPaneSuggestionsEnabled", 0);
                SetDword(cdm, "SoftLandingEnabled", 0);
                SetDword(cdm, "SubscribedContent-338388Enabled", 0);
                SetDword(cdm, "SubscribedContent-338389Enabled", 0);
                SetDword(cdm, "SubscribedContent-310093Enabled", 0);
                break;

            case "background-apps":
                SetDword(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
                SetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2);
                break;

            case "bloatware":
                RemoveBloatware(messages, ct);
                break;

            default:
                throw new InvalidOperationException($"Option inconnue : {id}");
        }
    }

    private void SetDword(string keyPath, string name, int value)
    {
        CaptureRegistry(keyPath, name);
        Registry.SetValue(keyPath, name, value, RegistryValueKind.DWord);
    }

    private void SetString(string keyPath, string name, string value)
    {
        CaptureRegistry(keyPath, name);
        Registry.SetValue(keyPath, name, value, RegistryValueKind.String);
    }

    private void DisableService(string serviceName)
    {
        // Sauvegarde de l'état de démarrage AVANT modification, pour restauration.
        CaptureService(serviceName);

        // Démarrage = 4 (Désactivé) dans le registre.
        try { Registry.SetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{serviceName}", "Start", 4, RegistryValueKind.DWord); }
        catch (Exception ex) { _logger.Warning($"Service {serviceName} : configuration impossible ({ex.Message})."); }

        // Arrêt immédiat si en cours d'exécution.
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception ex) { _logger.Warning($"Service {serviceName} : arrêt impossible ({ex.Message})."); }
    }

    private void DisableTasks(List<string> messages, params string[] taskPaths)
    {
        foreach (var path in taskPaths)
        {
            try
            {
                if (RunSchTasks($"/Change /TN \"{path}\" /Disable") && _capture is not null &&
                    !_capture.DisabledTasks.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _capture.DisabledTasks.Add(path);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Tâche {path} : désactivation impossible ({ex.Message}).");
            }
        }
    }

    private static bool RunSchTasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(8000);
        return p.HasExited && p.ExitCode == 0;
    }

    // =====================================================================
    //  Sauvegarde / restauration (réversibilité)
    // =====================================================================

    private void CaptureRegistry(string keyPath, string name)
    {
        if (_capture is null) return;
        if (_capture.Registry.Any(r =>
                r.KeyPath.Equals(keyPath, StringComparison.OrdinalIgnoreCase) &&
                r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return; // déjà sauvegardé lors d'une passe précédente : ne pas écraser l'original

        object? prev;
        try { prev = Registry.GetValue(keyPath, name, AbsentSentinel); }
        catch { prev = AbsentSentinel; }

        var (kind, value, existed) = ClassifyRegistryValue(prev, AbsentSentinel);
        _capture.Registry.Add(new RegistryValueBackup(keyPath, name, kind, value, existed));
    }

    /// <summary>Classe une valeur lue au registre en (type, valeur texte, existait). Fonction pure, testable.</summary>
    internal static (string? Kind, string? Value, bool Existed) ClassifyRegistryValue(object? raw, object sentinel)
    {
        if (raw is null || ReferenceEquals(raw, sentinel)) return (null, null, false);
        if (raw is int i) return ("DWord", i.ToString(CultureInfo.InvariantCulture), true);
        return ("String", raw.ToString(), true);
    }

    private void CaptureService(string serviceName)
    {
        if (_capture is null) return;
        if (_capture.Services.Any(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)))
            return;

        var key = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{serviceName}";
        object? prev;
        try { prev = Registry.GetValue(key, "Start", AbsentSentinel); }
        catch { prev = AbsentSentinel; }

        _capture.Services.Add(prev is int i
            ? new ServiceStartBackup(serviceName, i, true)
            : new ServiceStartBackup(serviceName, null, false));
    }

    public Task<DebloatResult> RevertAsync(IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var backup = LoadBackup();
            if (backup is null)
                return new DebloatResult(0, 0, new[] { "Aucune sauvegarde de debloat à restaurer." });

            int restored = 0, failed = 0;
            var messages = new List<string>();

            progress?.Report("Restauration des valeurs de registre…");
            foreach (var r in backup.Registry)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!r.Existed)
                        DeleteRegistryValue(r.KeyPath, r.Name);
                    else if (r.Kind == "DWord" && int.TryParse(r.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dw))
                        Registry.SetValue(r.KeyPath, r.Name, dw, RegistryValueKind.DWord);
                    else
                        Registry.SetValue(r.KeyPath, r.Name, r.Value ?? string.Empty, RegistryValueKind.String);
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warning($"Restauration registre {r.KeyPath}\\{r.Name} impossible ({ex.Message}).");
                }
            }

            progress?.Report("Restauration du démarrage des services…");
            foreach (var s in backup.Services)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Si le service n'avait pas de valeur Start d'origine, on remet le
                    // démarrage automatique (2) par prudence plutôt que de laisser désactivé.
                    var start = s.Existed && s.PreviousStart is int p ? p : 2;
                    Registry.SetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{s.ServiceName}",
                        "Start", start, RegistryValueKind.DWord);
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warning($"Restauration service {s.ServiceName} impossible ({ex.Message}).");
                }
            }

            progress?.Report("Réactivation des tâches planifiées…");
            foreach (var task in backup.DisabledTasks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (RunSchTasks($"/Change /TN \"{task}\" /Enable")) restored++;
                    else failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warning($"Réactivation tâche {task} impossible ({ex.Message}).");
                }
            }

            messages.Add($"↩️ Restauration : {restored} élément(s) restauré(s), {failed} échec(s).");
            messages.Add("ℹ️ Le retrait d'applications préinstallées n'est pas annulable — réinstallez-les depuis le Microsoft Store si besoin.");

            // Sauvegarde consommée : on la supprime pour éviter une restauration en double.
            try { if (File.Exists(_backupFile)) File.Delete(_backupFile); } catch { /* sans gravité */ }

            _logger.Info($"Debloat restauré : {restored} restauré(s), {failed} échec(s).");
            return new DebloatResult(restored, failed, messages);
        }, ct);
    }

    private static void DeleteRegistryValue(string keyPath, string name)
    {
        var idx = keyPath.IndexOf('\\');
        if (idx < 0) return;
        var hive = keyPath[..idx].ToUpperInvariant();
        var sub = keyPath[(idx + 1)..];
        RegistryKey? root = hive switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => null,
        };
        if (root is null) return;
        using var key = root.OpenSubKey(sub, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private DebloatBackup? LoadBackup()
    {
        try
        {
            if (!File.Exists(_backupFile)) return null;
            return JsonSerializer.Deserialize<DebloatBackup>(File.ReadAllText(_backupFile));
        }
        catch { return null; }
    }

    private void SaveBackup(DebloatBackup backup)
    {
        try
        {
            if (backup.Registry.Count == 0 && backup.Services.Count == 0 && backup.DisabledTasks.Count == 0)
                return; // rien à sauvegarder

            var dir = Path.GetDirectoryName(_backupFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_backupFile, JsonSerializer.Serialize(backup));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Sauvegarde debloat non écrite ({ex.Message}).");
        }
    }

    /// <summary>Liste BLANCHE conservatrice de bloatware sûr à retirer (jamais d'app système critique).</summary>
    private static readonly string[] BloatwarePackages =
    {
        "Microsoft.3DBuilder",
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.BingFinance",
        "Microsoft.BingSports",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.People",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsMaps",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Microsoft.MixedReality.Portal",
        "Microsoft.SkypeApp",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.Office.OneNote",
        "Clipchamp.Clipchamp",
        "Microsoft.Todos",
        "king.com.CandyCrushSaga",
        "king.com.CandyCrushSodaSaga",
    };

    /// <summary>Liste blanche dédupliquée des paquets bloatware (exposée pour les tests d'intégrité).</summary>
    internal static IReadOnlyList<string> BloatwareCatalog { get; } =
        BloatwarePackages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private void RemoveBloatware(List<string> messages, CancellationToken ct)
    {
        int removed = 0;
        foreach (var pkg in BloatwareCatalog)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // PowerShell : retire le paquet pour l'utilisateur courant UNIQUEMENT s'il est
                // présent, puis confirme sa disparition en émettant un marqueur. Sans ce contrôle,
                // PowerShell sort en code 0 même quand le paquet était déjà absent — ce qui
                // gonflait artificiellement le décompte « retiré(s) ».
                var cmd = $"$p = Get-AppxPackage '{pkg}'; if ($p) {{ $p | Remove-AppxPackage -ErrorAction SilentlyContinue; " +
                          $"if (-not (Get-AppxPackage '{pkg}')) {{ Write-Output 'CS_REMOVED' }} }}";
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p is not null)
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    if (output.Contains("CS_REMOVED", StringComparison.Ordinal)) removed++;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Bloatware {pkg} : retrait impossible ({ex.Message}).");
            }
        }
        messages.Add($"   {removed} application(s) réellement retirée(s).");
    }
}
