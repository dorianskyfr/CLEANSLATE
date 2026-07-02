using System.Collections.ObjectModel;
using System.Windows.Threading;
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
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _autoTimer;

    private string _osName = "—";
    private string _cpuName = "—";
    private string _gpuName = "—";
    private string _ramSummary = "—";
    private string _uptimeDisplay = "—";
    private string _maintenanceStatus = string.Empty;
    private bool _isMaintenanceRunning;
    private bool _autoMaintenanceEnabled;
    private int _autoMaintenanceIntervalHours = 24;
    private int _healthScore = 100;
    private string _healthRating = "—";
    private string _healthTips = string.Empty;

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        IMaintenanceService maintenance,
        IOverclockingAdvisor gpuDetector,
        IAppSettingsService settings,
        IDialogService dialogs)
    {
        _systemInfo = systemInfo;
        _maintenance = maintenance;
        _gpuDetector = gpuDetector;
        _settings = settings;
        _dialogs = dialogs;

        var saved = settings.Load();
        _autoMaintenanceEnabled = saved.AutoMaintenanceEnabled;
        _autoMaintenanceIntervalHours = saved.AutoMaintenanceIntervalHours is 6 or 12 or 24 or 48
            ? saved.AutoMaintenanceIntervalHours : 24;

        RefreshCommand = new RelayCommand(Refresh);
        MaintenanceCommand = new AsyncRelayCommand(RunMaintenanceAsync, () => !IsMaintenanceRunning);
        ExportReportCommand = new RelayCommand(ExportReport);

        Refresh();

        // Vérification périodique (toutes les 30 min) : lance l'entretien automatique
        // si l'intervalle est écoulé. Un premier passage est aussi tenté au démarrage.
        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _autoTimer.Tick += async (_, _) => await SafeAutoRunAsync();
        _autoTimer.Start();
        _ = SafeAutoRunAsync();
    }

    public ObservableCollection<DriveItem> Drives { get; } = new();

    /// <summary>Intervalles proposés pour l'entretien automatique (heures).</summary>
    public int[] IntervalOptions { get; } = { 6, 12, 24, 48 };

    public RelayCommand RefreshCommand { get; }
    public AsyncRelayCommand MaintenanceCommand { get; }
    public RelayCommand ExportReportCommand { get; }

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

    /// <summary>Score de santé /100, agrégé honnêtement (disque, mémoire, uptime).</summary>
    public int HealthScoreValue { get => _healthScore; private set { if (SetProperty(ref _healthScore, value)) OnPropertyChanged(nameof(HealthScoreDisplay)); } }
    public string HealthScoreDisplay => $"{_healthScore}";
    public string HealthRating { get => _healthRating; private set => SetProperty(ref _healthRating, value); }
    public string HealthTips
    {
        get => _healthTips;
        private set { if (SetProperty(ref _healthTips, value)) OnPropertyChanged(nameof(HasHealthTips)); }
    }
    public bool HasHealthTips => !string.IsNullOrEmpty(_healthTips);

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

    /// <summary>Active l'entretien automatique programmé (persisté entre les sessions).</summary>
    public bool AutoMaintenanceEnabled
    {
        get => _autoMaintenanceEnabled;
        set
        {
            if (SetProperty(ref _autoMaintenanceEnabled, value))
            {
                OnPropertyChanged(nameof(AutoMaintenanceLabel));
                _settings.Save(_settings.Load() with { AutoMaintenanceEnabled = value });
                _ = SafeAutoRunAsync(); // tente immédiatement si déjà dû
            }
        }
    }

    /// <summary>Intervalle (heures) entre deux entretiens automatiques (persisté).</summary>
    public int AutoMaintenanceIntervalHours
    {
        get => _autoMaintenanceIntervalHours;
        set
        {
            if (SetProperty(ref _autoMaintenanceIntervalHours, value))
            {
                OnPropertyChanged(nameof(AutoMaintenanceLabel));
                _settings.Save(_settings.Load() with { AutoMaintenanceIntervalHours = value });
            }
        }
    }

    public string AutoMaintenanceLabel =>
        $"Entretien automatique toutes les {_autoMaintenanceIntervalHours} h " +
        "(nettoyage sûr + optimisation RAM, en arrière-plan)";

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

            // Score de santé : agrégation honnête de signaux réels.
            double minFree = info.Drives.Count > 0 ? info.Drives.Min(d => 100 - d.UsedPercent) : 100;
            var health = HealthScore.Evaluate(new HealthInputs(
                info.Uptime.TotalDays, minFree, info.MemoryLoadPercent));
            HealthScoreValue = health.Score;
            HealthRating = health.Rating;
            HealthTips = health.Tips.Count > 0
                ? string.Join("\n", health.Tips)
                : "Tout est au vert. 🎉";
        }
        catch { /* informations partielles : on garde les tirets */ }

        try
        {
            var gpus = _gpuDetector.DetectGpus();
            GpuName = gpus.Count > 0 ? string.Join("  •  ", gpus.Select(g => g.Name)) : "—";
        }
        catch { GpuName = "—"; }
    }

    private Task RunMaintenanceAsync() => ExecuteMaintenanceAsync(silent: false);

    /// <summary>
    /// Vérifie si un entretien automatique est dû et, le cas échéant, le lance
    /// silencieusement (sans fenêtre). N'échoue jamais bruyamment (tick de timer).
    /// </summary>
    private async Task SafeAutoRunAsync()
    {
        try
        {
            if (IsMaintenanceRunning) return;
            var s = _settings.Load();
            if (!MaintenanceScheduler.ShouldRun(
                    s.AutoMaintenanceEnabled, s.AutoMaintenanceIntervalHours,
                    s.LastAutoMaintenanceUtc, DateTime.UtcNow))
                return;

            await ExecuteMaintenanceAsync(silent: true);
            _settings.Save(_settings.Load() with { LastAutoMaintenanceUtc = DateTime.UtcNow });
        }
        catch { /* entretien automatique best-effort : jamais de crash */ }
    }

    private async Task ExecuteMaintenanceAsync(bool silent)
    {
        IsMaintenanceRunning = true;
        try
        {
            var progress = new Progress<string>(msg => MaintenanceStatus = msg);
            var report = await _maintenance.RunAsync(progress, CancellationToken.None);

            var prefix = silent ? "[Auto] " : string.Empty;
            MaintenanceStatus =
                $"{prefix}Entretien terminé : {FormatBytes(report.FreedBytes)} libérés " +
                $"({report.DeletedCount} élément(s) supprimé(s)" +
                (report.FailedCount > 0 ? $", {report.FailedCount} échec(s)" : "") + ").";

            if (!silent)
            {
                var details = string.Join("\n", report.Steps.Select(s => $"• {s.Label} : {s.Detail}"));
                _dialogs.Info("Entretien en 1 clic", $"Bilan :\n\n{details}");
            }

            Refresh();
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                MaintenanceStatus = string.Empty;
                _dialogs.Warn("Entretien en 1 clic", ex.Message);
            }
        }
        finally
        {
            IsMaintenanceRunning = false;
        }
    }

    private void ExportReport()
    {
        try
        {
            var info = _systemInfo.Read();
            double minFree = info.Drives.Count > 0 ? info.Drives.Min(d => 100 - d.UsedPercent) : 100;
            var health = HealthScore.Evaluate(new HealthInputs(
                info.Uptime.TotalDays, minFree, info.MemoryLoadPercent));
            var text = SystemReport.Build(info, health, DateTime.Now);

            var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var path = System.IO.Path.Combine(dir, $"CleanSlate-rapport-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            System.IO.File.WriteAllText(path, text);

            _dialogs.Info("Rapport exporté", $"Rapport système enregistré :\n{path}");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* l'ouverture est un confort : le fichier est déjà écrit */ }
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Export du rapport", ex.Message);
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
