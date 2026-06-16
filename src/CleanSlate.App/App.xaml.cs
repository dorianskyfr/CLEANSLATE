using System.Windows;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;
using CleanSlate.App.ViewModels;

namespace CleanSlate.App;

public partial class App : Application
{
    private static bool _isDark = true;

    /// <summary>Bascule entre thème sombre et thème clair à chaud (DynamicResource).</summary>
    public static void SwitchTheme(bool toDark)
    {
        _isDark = toDark;
        var app = (App)Current;
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged.RemoveAt(0);
        var name = toDark ? "Dark" : "Light";
        merged.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Themes/{name}Theme.xaml", UriKind.Relative)
        });
    }

    public static bool IsDark => _isDark;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IActionLogger logger = new FileActionLogger();
        IDialogService dialogs = new DialogService();

        // Préférences persistées (thème, taille de fenêtre) : appliquées avant
        // l'affichage pour éviter tout « flash » du mauvais thème.
        IAppSettingsService settingsSvc = new AppSettingsService();
        var settings = settingsSvc.Load();
        if (!settings.IsDarkTheme) SwitchTheme(false);

        var providers = new ICleaningProvider[]
        {
            new TempFilesProvider(logger),
            new BrowserCacheProvider(logger),
            new ThumbnailCacheProvider(logger),
            new ShaderCacheProvider(logger),
            new ErrorReportsProvider(logger),
            new RecycleBinProvider(logger),
            new WindowsLogsProvider(logger),
            new PrefetchProvider(logger),
        };
        var engine = new CleaningEngine(providers, logger);

        IMemoryMonitor memoryMonitor       = new MemoryMonitor();
        IGameMode gameMode                 = new GameModeService(logger);
        IOverclockingAdvisor overclocking  = new OverclockingAdvisor();
        IGpuOverclocker gpuOverclocker     = new GpuOverclocker();
        IGpuDriverChecker driverChecker    = new GpuDriverChecker();
        IStartupManager startupManager     = new StartupManager(logger);
        IRegistryCleaner registryCleaner   = new RegistryCleaner(logger);
        IBackupService backupService       = new RegistryBackupService(logger);
        IWindowsDebloater debloater        = new WindowsDebloatService(logger);
        IQuickRepairService repairSvc      = new QuickRepairService(logger);
        IUpdateService updateSvc           = new GitHubUpdateService();
        IAdBlockService adBlockSvc         = new DnsAdBlockService();
        ISystemInfoService systemInfo      = new SystemInfoService(memoryMonitor);
        IMaintenanceService maintenance    = new MaintenanceService(engine, memoryMonitor);
        IDlssEnablerService dlssEnabler    = new DlssEnablerService();
        IGameCatalogService gameCatalog    = new SteamGameCatalogService();
        IFileDownloadService fileDownloader = new FileDownloadService();

        // Nettoyage de l'ancien blocage par fichier hosts (versions <= v0.9.2),
        // qui rendait le PC très lent et ne pouvait être désactivé sans Mode sans échec.
        DnsAdBlockService.CleanupLegacyHostsBlock();

        _ = gameMode.TryRecoverAsync(CancellationToken.None);

        var dashboardVm    = new DashboardViewModel(systemInfo, maintenance, overclocking, dialogs);
        var cleaningVm     = new CleaningViewModel(engine, dialogs);
        var memoryVm       = new MemoryViewModel(memoryMonitor, settingsSvc, dialogs);
        var driversVm      = new DriversViewModel(dialogs);
        var gameModeVm     = new GameModeViewModel(gameMode, overclocking, gpuOverclocker, driverChecker, dlssEnabler, gameCatalog, fileDownloader, settingsSvc, dialogs);
        var optimizationVm = new OptimizationViewModel(startupManager, registryCleaner, backupService, debloater, dialogs);
        var quickRepairVm  = new QuickRepairViewModel(repairSvc, dialogs);
        var adBlockVm      = new AdBlockViewModel(adBlockSvc, settingsSvc, dialogs);

        var mainVm = new MainViewModel(
            dashboardVm, cleaningVm, memoryVm, driversVm, gameModeVm, optimizationVm, quickRepairVm, adBlockVm,
            updateSvc, settingsSvc, dialogs);

        var window = new MainWindow { DataContext = mainVm };

        // Restauration de la taille de fenêtre de la session précédente.
        window.Width = settings.WindowWidth;
        window.Height = settings.WindowHeight;
        if (settings.WindowMaximized) window.WindowState = WindowState.Maximized;

        // Sauvegarde des préférences à la fermeture (thème + dimensions réelles,
        // hors plein écran : RestoreBounds garde la taille « fenêtrée »).
        window.Closing += (_, _) =>
        {
            bool maximized = window.WindowState == WindowState.Maximized;
            var bounds = maximized ? window.RestoreBounds : new Rect(
                window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            settingsSvc.Save(settingsSvc.Load() with
            {
                IsDarkTheme = IsDark,
                WindowWidth = bounds.Width,
                WindowHeight = bounds.Height,
                WindowMaximized = maximized,
            });
        };

        window.Show();

        // Vérification discrète des mises à jour au démarrage (silencieuse si à jour).
        _ = mainVm.CheckUpdatesOnStartupAsync();

        logger.Info("CleanSlate démarré.");
    }
}
