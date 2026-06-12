using System.Collections.ObjectModel;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Ligne « disque » du tableau de bord (valeurs figées au rafraîchissement).</summary>
public sealed record DriveItem(string Title, string Detail, double UsedPercent);

/// <summary>
/// Page d'accueil : vue d'ensemble du système (Windows, CPU, GPU, RAM, disques,
/// uptime) et « Entretien en 1 clic » — nettoyage des seules catégories sûres +
/// optimisation RAM, avec un bilan honnête de ce qui a été fait.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IMaintenanceService _maintenance;
    private readonly IOverclockingAdvisor _gpuDetector;
    private readonly IDialogService _dialogs;

    private string _osName = "—";
    private string _cpuName = "—";
    private string _gpuName = "—";
    private string _ramSummary = "—";
    private string _uptimeDisplay = "—";
    private string _maintenanceStatus = string.Empty;
    private bool _isMaintenanceRunning;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        IMaintenanceService maintenance,
        IOverclockingAdvisor gpuDetector,
        IDialogService dialogs)
    {
        _systemInfo = systemInfo;
        _maintenance = maintenance;
        _gpuDetector = gpuDetector;
        _dialogs = dialogs;

        RefreshCommand = new RelayCommand(Refresh);
        MaintenanceCommand = new AsyncRelayCommand(RunMaintenanceAsync, () => !IsMaintenanceRunning);

        Refresh();
    }

    public ObservableCollection<DriveItem> Drives { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand MaintenanceCommand { get; }

    public string OsName       { get => _osName;       private set => SetProperty(ref _osName, value); }
    public string CpuName      { get => _cpuName;      private set => SetProperty(ref _cpuName, value); }
    public string GpuName      { get => _gpuName;      private set => SetProperty(ref _gpuName, value); }
    public string RamSummary   { get => _ramSummary;   private set => SetProperty(ref _ramSummary, value); }
    public string UptimeDisplay{ get => _uptimeDisplay;private set => SetProperty(ref _uptimeDisplay, value); }

    private string _uptimeHint = string.Empty;

    /// <summary>Conseil affiché quand le PC n'a pas redémarré depuis longtemps.</summary>
    public string UptimeHint
    {
        get => _uptimeHint;
        private set
        {
            if (SetProperty(ref _uptimeHint, value))
                OnPropertyChanged(nameof(HasUptimeHint));
        }
    }

    public bool HasUptimeHint => !string.IsNullOrEmpty(_uptimeHint);

    public string MaintenanceStatus
    {
        get => _maintenanceStatus;
        private set
        {
            if (SetProperty(ref _maintenanceStatus, value))
                OnPropertyChanged(nameof(HasMaintenanceStatus));
        }
    }

    public bool HasMaintenanceStatus => !string.IsNullOrEmpty(_maintenanceStatus);

    public bool IsMaintenanceRunning
    {
        get => _isMaintenanceRunning;
        private set
        {
            if (SetProperty(ref _isMaintenanceRunning, value))
                MaintenanceCommand.RaiseCanExecuteChanged();
        }
    }

    public string HonestNotice =>
        "L'entretien en 1 clic n'exécute que les nettoyages SÛRS (fichiers temporaires, " +
        "miniatures…) puis optimise la RAM. La corbeille, le cache des navigateurs et les " +
        "actions sensibles ne sont jamais touchés ici : ils restent un choix explicite " +
        "dans l'onglet Nettoyage.";

    private void Refresh()
    {
        try
        {
            var info = _systemInfo.Read();
            OsName = info.OsName;
            CpuName = $"{info.CpuName} ({info.LogicalCores} threads)";
            RamSummary = $"{FormatBytes((long)info.TotalRamBytes)} installés";
            UptimeDisplay = FormatUptime(info.Uptime);
            UptimeHint = info.Uptime.TotalDays >= 7
                ? $"💡 PC allumé depuis {(int)info.Uptime.TotalDays} jours sans redémarrage complet : " +
                  "un redémarrage purge la mémoire et applique les mises à jour en attente."
                : string.Empty;

            Drives.Clear();
            foreach (var d in info.Drives)
            {
                var title = d.Label is null ? d.Name : $"{d.Name} — {d.Label}";
                Drives.Add(new DriveItem(
                    title,
                    $"{FormatBytes(d.FreeBytes)} libres sur {FormatBytes(d.TotalBytes)}",
                    d.UsedPercent));
            }
        }
        catch { /* informations partielles : on garde les tirets */ }

        try
        {
            var gpus = _gpuDetector.DetectGpus();
            GpuName = gpus.Count > 0 ? string.Join("  •  ", gpus.Select(g => g.Name)) : "—";
        }
        catch { GpuName = "—"; }
    }

    private async Task RunMaintenanceAsync()
    {
        IsMaintenanceRunning = true;
        try
        {
            var progress = new Progress<string>(msg => MaintenanceStatus = msg);
            var report = await _maintenance.RunAsync(progress, CancellationToken.None);

            MaintenanceStatus =
                $"Entretien terminé : {FormatBytes(report.FreedBytes)} libérés " +
                $"({report.DeletedCount} élément(s) supprimé(s)" +
                (report.FailedCount > 0 ? $", {report.FailedCount} échec(s)" : "") + ").";

            var details = string.Join("\n", report.Steps.Select(s => $"• {s.Label} : {s.Detail}"));
            _dialogs.Info("Entretien en 1 clic", $"Bilan :\n\n{details}");

            Refresh();
        }
        catch (Exception ex)
        {
            MaintenanceStatus = string.Empty;
            _dialogs.Warn("Entretien en 1 clic", ex.Message);
        }
        finally
        {
            IsMaintenanceRunning = false;
        }
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1
            ? $"{(int)t.TotalDays} j {t.Hours} h {t.Minutes} min"
            : $"{t.Hours} h {t.Minutes} min";

    private static string FormatBytes(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}
