using System.Windows;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;
using CleanSlate.App.ViewModels;

namespace CleanSlate.App;

/// <summary>
/// Point d'entrée WPF et « composition root » : on instancie et relie ici toutes
/// les dépendances (services Core → ViewModels). Approche explicite, sans
/// conteneur DI, pour rester léger et lisible.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Services transverses.
        IActionLogger logger = new FileActionLogger();
        IDialogService dialogs = new DialogService();

        // 2. Module 1 — Nettoyage : providers + moteur.
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

        // 3. Modules 2 à 5.
        IMemoryMonitor memoryMonitor = new MemoryMonitor();
        IDriverInventory driverInventory = new WmiDriverInventory();
        IGameMode gameMode = new GameModeService(logger);
        IStartupManager startupManager = new StartupManager(logger);
        IRegistryCleaner registryCleaner = new RegistryCleaner(logger);
        IBackupService backupService = new RegistryBackupService(logger);

        // 4. Récupération de sûreté : si une session précédente s'est fermée
        //    pendant le Mode Jeu, on restaure l'état (reprise des processus).
        _ = gameMode.TryRecoverAsync(CancellationToken.None);

        // 5. ViewModels.
        var cleaningVm = new CleaningViewModel(engine, dialogs);
        var memoryVm = new MemoryViewModel(memoryMonitor, dialogs);
        var driversVm = new DriversViewModel(driverInventory, dialogs);
        var gameModeVm = new GameModeViewModel(gameMode, dialogs);
        var optimizationVm = new OptimizationViewModel(startupManager, registryCleaner, backupService, dialogs);

        var mainVm = new MainViewModel(
            cleaningVm, memoryVm, driversVm, gameModeVm, optimizationVm, dialogs);

        // 6. Fenêtre principale.
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        logger.Info("CleanSlate démarré.");
    }
}
