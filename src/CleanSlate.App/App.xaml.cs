using System.Windows;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Diagnostics;
using CleanSlate.App.Infrastructure;
using CleanSlate.App.ViewModels;

namespace CleanSlate.App;

/// <summary>
/// Point d'entrée WPF. Sert de « composition root » : c'est ici qu'on instancie
/// et qu'on relie manuellement les dépendances (logger → providers → moteur →
/// ViewModels). Approche simple et explicite, sans conteneur DI.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Services transverses.
        IActionLogger logger = new FileActionLogger();
        IDialogService dialogs = new DialogService();

        // 2. Providers du module 1 (nettoyage). Ajouter une catégorie = l'ajouter ici.
        var providers = new ICleaningProvider[]
        {
            new TempFilesProvider(logger),
            new BrowserCacheProvider(logger),
            new ThumbnailCacheProvider(logger),
            new RecycleBinProvider(logger),
            new WindowsLogsProvider(logger),
            new PrefetchProvider(logger),
        };

        // 3. Moteur d'orchestration.
        var engine = new CleaningEngine(providers, logger);

        // 4. ViewModels.
        var cleaningVm = new CleaningViewModel(engine, dialogs);
        var mainVm = new MainViewModel(cleaningVm, dialogs);

        // 5. Fenêtre principale.
        var window = new MainWindow { DataContext = mainVm };
        window.Show();

        logger.Info("CleanSlate démarré.");
    }
}
