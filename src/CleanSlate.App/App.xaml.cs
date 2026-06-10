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

        var providers = new ICleaningProvider[]
        {
            new TempFilesProvider(logger),
            new BrowserCacheProvider(logger),
            new ThumbnailCacheProvider(logger),
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
        IAdBlockService adBlockSvc         = new HostsAdBlockService();

        _ = gameMode.TryRecoverAsync(CancellationToken.None);

        var cleaningVm     = new CleaningViewModel(engine, dialogs);
        var memoryVm       = new MemoryViewModel(memoryMonitor, dialogs);
        var driversVm      = new DriversViewModel(dialogs);
        var gameModeVm     = new GameModeViewModel(gameMode, overclocking, gpuOverclocker, driverChecker, dialogs);
        var optimizationVm = new OptimizationViewModel(startupManager, registryCleaner, backupService, debloater, dialogs);
        var quickRepairVm  = new QuickRepairViewModel(repairSvc, dialogs);
        var adBlockVm      = new AdBlockViewModel(adBlockSvc, dialogs);

        var mainVm = new MainViewModel(
            cleaningVm, memoryVm, driversVm, gameModeVm, optimizationVm, quickRepairVm, adBlockVm, updateSvc, dialogs);

        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        // Vérification discrète des mises à jour au démarrage (silencieuse si à jour).
        _ = mainVm.CheckUpdatesOnStartupAsync();

        logger.Info("CleanSlate démarré.");
    }
}
