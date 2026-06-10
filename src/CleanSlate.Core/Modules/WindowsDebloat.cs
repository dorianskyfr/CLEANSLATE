using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Modules;

public enum DebloatCategory { Telemetrie, Confidentialite, Interface, Applications }

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
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDebloatService : IWindowsDebloater
{
    private readonly IActionLogger _logger;
    public WindowsDebloatService(IActionLogger logger) => _logger = logger;

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
                Registry.SetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                    "Value", "Deny", RegistryValueKind.String);
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

    private static void SetDword(string keyPath, string name, int value) =>
        Registry.SetValue(keyPath, name, value, RegistryValueKind.DWord);

    private void DisableService(string serviceName)
    {
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
                var psi = new ProcessStartInfo("schtasks.exe", $"/Change /TN \"{path}\" /Disable")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(8000);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Tâche {path} : désactivation impossible ({ex.Message}).");
            }
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
        "Microsoft.MicrosoftSolitaireCollection",
    };

    private void RemoveBloatware(List<string> messages, CancellationToken ct)
    {
        int removed = 0;
        foreach (var pkg in BloatwarePackages.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // PowerShell : retire le paquet pour l'utilisateur courant s'il est présent.
                var cmd = $"Get-AppxPackage '{pkg}' | Remove-AppxPackage -ErrorAction SilentlyContinue";
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
                    p.WaitForExit(15000);
                    if (p.ExitCode == 0) removed++;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Bloatware {pkg} : retrait impossible ({ex.Message}).");
            }
        }
        messages.Add($"   {removed} type(s) d'application traité(s).");
    }
}
