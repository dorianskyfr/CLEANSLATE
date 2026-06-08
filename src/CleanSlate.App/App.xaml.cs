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

        IMemoryMonitor memoryMonitor     = new MemoryMonitor();
        IDriverInventory driverInventory = new WmiDriverInventory();
        IGameMode gameMode               = new GameModeService(logger);
        IStartupManager startupManager   = new StartupManager(logger);
        IRegistryCleaner registryCleaner = new RegistryCleaner(logger);
        IBackupService backupService     = new RegistryBackupService(logger);
        IQuickRepairService repairSvc    = new QuickRepairService(logger);
        IUpdateService updateSvc         = new GitHubUpdateService();

        _ = gameMode.TryRecoverAsync(CancellationToken.None);

        var cleaningVm     = new CleaningViewModel(engine, dialogs);
        var memoryVm       = new MemoryViewModel(memoryMonitor, dialogs);
        var driversVm      = new DriversViewModel(driverInventory, dialogs);
        var gameModeVm     = new GameModeViewModel(gameMode, dialogs);
        var optimizationVm = new OptimizationViewModel(startupManager, registryCleaner, backupService, dialogs);
        var quickRepairVm  = new QuickRepairViewModel(repairSvc, dialogs);

        var mainVm = new MainViewModel(
            cleaningVm, memoryVm, driversVm, gameModeVm, optimizationVm, quickRepairVm, updateSvc, dialogs);

        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        logger.Info("CleanSlate démarré.");
    }
}
