using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Modules;

public enum RepairStatus { Pending, Checking, Good, Warning, Error }

/// <summary>Un check de santé système avec son état et la possibilité de le réparer.</summary>
public sealed class RepairCheck : System.ComponentModel.INotifyPropertyChanged
{
    private RepairStatus _status = RepairStatus.Pending;
    private string _detail = string.Empty;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool CanRepair { get; init; }

    public RepairStatus Status
    {
        get => _status;
        set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); PropertyChanged?.Invoke(this, new(nameof(StatusIcon))); PropertyChanged?.Invoke(this, new(nameof(StatusColor))); }
    }

    public string Detail
    {
        get => _detail;
        set { _detail = value; PropertyChanged?.Invoke(this, new(nameof(Detail))); }
    }

    public string StatusIcon => Status switch
    {
        RepairStatus.Good    => "✅",
        RepairStatus.Warning => "⚠️",
        RepairStatus.Error   => "❌",
        RepairStatus.Checking => "🔄",
        _                    => "⏳",
    };

    public string StatusColor => Status switch
    {
        RepairStatus.Good    => "#4CAF50",
        RepairStatus.Warning => "#FFB454",
        RepairStatus.Error   => "#EF5350",
        RepairStatus.Checking => "#4F8CFF",
        _                    => "#9A9BB0",
    };

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public interface IQuickRepairService
{
    Task<IReadOnlyList<RepairCheck>> CreateChecksAsync();
    Task RunCheckAsync(RepairCheck check, CancellationToken ct);
    Task RepairAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class QuickRepairService : IQuickRepairService
{
    private readonly IActionLogger _logger;

    public QuickRepairService(IActionLogger logger) => _logger = logger;

    public Task<IReadOnlyList<RepairCheck>> CreateChecksAsync() => Task.FromResult<IReadOnlyList<RepairCheck>>(new List<RepairCheck>
    {
        new() { Id = "temp",   Name = "Fichiers temporaires",     Description = "Vérifie la taille des dossiers temp Windows et utilisateur.", CanRepair = true  },
        new() { Id = "wuserv", Name = "Service Windows Update",   Description = "Vérifie que le service de mise à jour Windows est actif.",     CanRepair = true  },
        new() { Id = "disk",   Name = "Santé des disques",         Description = "Interroge le statut SMART des disques via WMI.",              CanRepair = false },
        new() { Id = "net",    Name = "Connectivité réseau",       Description = "Vérifie la résolution DNS et la connectivité Internet.",       CanRepair = true  },
        new() { Id = "recycle",Name = "Corbeille",                 Description = "Vérifie si la corbeille contient des fichiers à supprimer.",   CanRepair = true  },
        new() { Id = "sfc",    Name = "Fichiers système (SFC)",    Description = "Lance SFC /scannow pour vérifier les fichiers système Windows (peut prendre plusieurs minutes, admin requis).", CanRepair = true },
    });

    public async Task RunCheckAsync(RepairCheck check, CancellationToken ct)
    {
        check.Status = RepairStatus.Checking;
        try
        {
            switch (check.Id)
            {
                case "temp":    await CheckTempAsync(check, ct);    break;
                case "wuserv":  await CheckWuservAsync(check, ct);  break;
                case "disk":    await CheckDiskAsync(check, ct);    break;
                case "net":     await CheckNetworkAsync(check, ct); break;
                case "recycle": await CheckRecycleAsync(check, ct); break;
                case "sfc":     CheckSfcStatus(check);              break;
                default:        check.Status = RepairStatus.Good; check.Detail = "OK"; break;
            }
        }
        catch (OperationCanceledException) { check.Status = RepairStatus.Warning; check.Detail = "Annulé."; }
        catch (Exception ex) { check.Status = RepairStatus.Error; check.Detail = ex.Message; }
    }

    public async Task RepairAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            switch (check.Id)
            {
                case "temp":    await RepairTempAsync(check, progress, ct);    break;
                case "wuserv":  await RepairWuservAsync(check, progress, ct);  break;
                case "net":     await RepairNetworkAsync(check, progress, ct); break;
                case "recycle": await RepairRecycleAsync(check, progress, ct); break;
                case "sfc":     await RepairSfcAsync(check, progress, ct);     break;
                default: check.Detail = "Aucune réparation disponible."; break;
            }
        }
        catch (OperationCanceledException) { check.Detail = "Réparation annulée."; }
        catch (Exception ex) { check.Status = RepairStatus.Error; check.Detail = $"Erreur : {ex.Message}"; _logger.Error($"Réparation {check.Id}", ex); }
    }

    // ---- Checks ----

    private static Task CheckTempAsync(RepairCheck check, CancellationToken ct) => Task.Run(() =>
    {
        long size = 0;
        foreach (var folder in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
        {
            ct.ThrowIfCancellationRequested();
            try { size += Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); } catch { }
        }
        var mb = size / 1024 / 1024;
        check.Detail = $"{mb} Mo de fichiers temporaires.";
        check.Status = mb > 500 ? RepairStatus.Warning : RepairStatus.Good;
    }, ct);

    private static Task CheckWuservAsync(RepairCheck check, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            using var sc = new ServiceController("wuauserv");
            check.Detail = $"Service Windows Update : {sc.Status}.";
            check.Status = sc.Status == ServiceControllerStatus.Running ? RepairStatus.Good : RepairStatus.Warning;
        }
        catch { check.Status = RepairStatus.Warning; check.Detail = "Service Windows Update introuvable."; }
    }, ct);

    private static Task CheckDiskAsync(RepairCheck check, CancellationToken ct) => Task.Run(() =>
    {
        var issues = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Status FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                ct.ThrowIfCancellationRequested();
                var status = obj["Status"] as string ?? "Unknown";
                var name = obj["Caption"] as string ?? "Disque";
                if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                    issues.Add($"{name}: {status}");
            }
        }
        catch (Exception ex) { check.Status = RepairStatus.Warning; check.Detail = ex.Message; return; }

        if (issues.Count == 0) { check.Status = RepairStatus.Good; check.Detail = "Tous les disques rapportent un statut OK."; }
        else { check.Status = RepairStatus.Error; check.Detail = string.Join(", ", issues); }
    }, ct);

    private static async Task CheckNetworkAsync(RepairCheck check, CancellationToken ct)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await client.GetAsync("https://dns.google", ct);
            check.Status = RepairStatus.Good;
            check.Detail = "Connexion Internet opérationnelle.";
        }
        catch { check.Status = RepairStatus.Error; check.Detail = "Pas de connexion Internet détectée."; }
    }

    private static Task CheckRecycleAsync(RepairCheck check, CancellationToken ct) => Task.Run(() =>
    {
        long size = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new CleanSlate.Core.Native.NativeMethods.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CleanSlate.Core.Native.NativeMethods.SHQUERYRBINFO>() };
                CleanSlate.Core.Native.NativeMethods.SHQueryRecycleBin(drive.RootDirectory.FullName, ref info);
                size += info.i64Size;
            }
            catch { }
        }
        var mb = size / 1024 / 1024;
        check.Detail = mb > 0 ? $"{mb} Mo dans la corbeille." : "Corbeille vide.";
        check.Status = mb > 100 ? RepairStatus.Warning : RepairStatus.Good;
    }, ct);

    private static void CheckSfcStatus(RepairCheck check)
    {
        // Cherche dans l'event log si des erreurs système ont été signalées récemment.
        // On parcourt du plus RÉCENT au plus ancien et on s'arrête dès qu'on sort de la
        // fenêtre de 7 jours — au lieu d'énumérer tout le journal en mémoire (lent).
        try
        {
            using var log = new EventLog("System");
            var cutoff = DateTime.Now.AddDays(-7);
            var entries = log.Entries;
            int count = entries.Count;
            int found = 0;

            for (int i = count - 1; i >= 0 && found < 5; i--)
            {
                EventLogEntry e;
                try { e = entries[i]; } catch { continue; }
                if (e.TimeGenerated < cutoff) break; // au-delà de la fenêtre : inutile de continuer
                if (e.EntryType == EventLogEntryType.Error &&
                    e.Source == "Microsoft-Windows-WER-SystemErrorReporting")
                    found++;
            }

            if (found == 0) { check.Status = RepairStatus.Good; check.Detail = "Aucune erreur système récente détectée (7 derniers jours)."; }
            else { check.Status = RepairStatus.Warning; check.Detail = $"{found} erreur(s) système détectée(s) ces 7 derniers jours."; }
        }
        catch { check.Status = RepairStatus.Good; check.Detail = "Vérification de l'event log non disponible."; }
    }

    // ---- Réparations ----

    private static Task RepairTempAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Nettoyage des fichiers temporaires…");
        // La suppression est faite sur un thread de fond (et non sur le thread appelant,
        // potentiellement le thread UI) : sinon l'interface se figeait pendant le nettoyage.
        return Task.Run(() =>
        {
            long freed = 0;
            foreach (var folder in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
            {
                ct.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { var len = new FileInfo(file).Length; File.Delete(file); freed += len; } catch { }
                }
            }
            check.Status = RepairStatus.Good;
            check.Detail = $"Nettoyage terminé : {freed / 1024 / 1024} Mo libérés.";
        }, ct);
    }

    private static async Task RepairWuservAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Démarrage du service Windows Update…");
        await Task.Run(() =>
        {
            using var sc = new ServiceController("wuauserv");
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            check.Status = RepairStatus.Good;
            check.Detail = "Service Windows Update démarré.";
        }, ct);
    }

    private static async Task RepairNetworkAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Réinitialisation TCP/IP et Winsock (netsh)…");
        await Task.Run(() =>
        {
            RunCommand("netsh", "winsock reset");
            RunCommand("netsh", "int ip reset");
            check.Status = RepairStatus.Warning;
            check.Detail = "Réinitialisation effectuée. Un redémarrage de Windows est nécessaire pour appliquer les changements.";
        }, ct);
    }

    private static async Task RepairRecycleAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Vidage de la corbeille…");
        await Task.Run(() =>
        {
            CleanSlate.Core.Native.NativeMethods.SHEmptyRecycleBin(
                IntPtr.Zero, null,
                CleanSlate.Core.Native.NativeMethods.RecycleFlags.SHERB_NOCONFIRMATION |
                CleanSlate.Core.Native.NativeMethods.RecycleFlags.SHERB_NOPROGRESSUI |
                CleanSlate.Core.Native.NativeMethods.RecycleFlags.SHERB_NOSOUND);
            check.Status = RepairStatus.Good;
            check.Detail = "Corbeille vidée.";
        }, ct);
    }

    private static async Task RepairSfcAsync(RepairCheck check, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Lancement de SFC /scannow (peut prendre 10-15 min)…");
        await Task.Run(() =>
        {
            // CleanSlate tourne déjà en administrateur (manifeste requireAdministrator),
            // donc sfc hérite de l'élévation. On NE met PAS Verb="runas" : ce verbe exige
            // UseShellExecute=true, incompatible avec la redirection de sortie — la
            // combinaison précédente était contradictoire et le verbe était ignoré.
            var psi = new ProcessStartInfo("sfc", "/scannow")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            try
            {
                using var proc = Process.Start(psi)!;
                proc.WaitForExit(TimeSpan.FromMinutes(20));
                check.Status = proc.ExitCode == 0 ? RepairStatus.Good : RepairStatus.Warning;
                check.Detail = proc.ExitCode == 0
                    ? "SFC terminé sans erreur."
                    : $"SFC terminé avec code {proc.ExitCode}. Consultez %WINDIR%\\Logs\\CBS\\CBS.log.";
            }
            catch (Exception ex) { check.Status = RepairStatus.Error; check.Detail = $"SFC nécessite les droits administrateur. {ex.Message}"; }
        }, ct);
    }

    private static void RunCommand(string cmd, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(cmd, args) { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(10_000);
        }
        catch { }
    }
}
