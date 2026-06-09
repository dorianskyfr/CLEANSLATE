using System.Collections.ObjectModel;
using System.Windows.Input;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string PatchNotes =
        "─────────────────────────────\n" +
        "v0.2.7 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Notes de version complètes intégrées dans l'application\n\n" +

        "─────────────────────────────\n" +
        "v0.2.6 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Fix : le bouton « Analyser » scanne maintenant TOUTES les catégories\n" +
        "  (plus de « — » sur les lignes non cochées)\n" +
        "• Fix : détection de la corbeille via le dossier $Recycle.Bin\\{SID}\n" +
        "  de l'utilisateur — plus fiable sur Windows 11 multi-comptes\n\n" +

        "─────────────────────────────\n" +
        "v0.2.5 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Fix : corbeille affichait « — » même avec des fichiers présents\n" +
        "  (HRESULT S_FALSE sur certaines configs Windows 11)\n" +
        "• Mode Jeu : liste étendue de 6 à 22 processus suspendus\n" +
        "  (cloud, communication, lanceurs de jeux, Adobe…)\n\n" +

        "─────────────────────────────\n" +
        "v0.2 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Thème sombre / clair basculable à chaud\n" +
        "• Barre de progression avec % lors de l'analyse\n" +
        "• Vue des lecteurs disponibles avec espace libre\n" +
        "• Optimisation RAM avancée (purge Standby List)\n" +
        "• Mises à jour de pilotes via Windows Update (WUApi)\n" +
        "• Onglet Réparation rapide : diagnostic en 6 points\n" +
        "• Vérificateur de mises à jour intégré\n\n" +

        "─────────────────────────────\n" +
        "v0.1 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Nettoyage : fichiers temp, cache navigateurs, miniatures,\n" +
        "  corbeille, journaux Windows, prefetch\n" +
        "• Mémoire : surveillance en temps réel + optimisation avancée\n" +
        "• Pilotes : inventaire WMI\n" +
        "• Mode Jeu : suspension des processus non essentiels\n" +
        "• Optimisation : gestionnaire démarrage + nettoyage registre\n" +
        "• Réparation rapide : diagnostic système en 6 points";

    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogs;
    private NavigationItem _selectedItem = null!;
    private bool _isDark = App.IsDark;
    private string _updateStatus = string.Empty;
    private bool _isCheckingUpdate;

    public MainViewModel(
        CleaningViewModel cleaning,
        MemoryViewModel memory,
        DriversViewModel drivers,
        GameModeViewModel gameMode,
        OptimizationViewModel optimization,
        QuickRepairViewModel quickRepair,
        IUpdateService updateService,
        IDialogService dialogs)
    {
        _updateService = updateService;
        _dialogs = dialogs;

        IsAdministrator = ElevationHelper.IsRunningAsAdministrator();

        Items = new ObservableCollection<NavigationItem>
        {
            new("Nettoyage",        "🧹", cleaning),
            new("Mémoire",          "📊", memory),
            new("Pilotes",          "🧩", drivers),
            new("Mode Jeu",         "🎮", gameMode),
            new("Optimisation",     "⚙️", optimization),
            new("Réparation rapide","🛠️", quickRepair),
        };
        _selectedItem = Items[0];

        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (!ElevationHelper.RestartAsAdministrator())
                dialogs.Warn("Élévation refusée",
                    "L'application continue sans droits administrateur. " +
                    "Certaines actions système ne seront pas disponibles.");
            else
                System.Windows.Application.Current.Shutdown();
        }, () => !IsAdministrator);

        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        ShowAboutCommand = new RelayCommand(() => dialogs.Info("À propos de CleanSlate",
            $"CleanSlate v{_updateService.CurrentVersion}\n" +
            "Outil open-source d'optimisation et de nettoyage Windows.\n\n" +
            "GitHub : https://github.com/dorianskyfr/CLEANSLATE\n" +
            "Licence : MIT"));

        ShowPatchNotesCommand = new RelayCommand(() =>
            dialogs.Info("Notes de mise à jour", PatchNotes));

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync,
            () => !IsCheckingUpdate);
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool IsAdministrator { get; }
    public bool ShowElevationButton => !IsAdministrator;
    public string PrivilegeStatus => IsAdministrator ? "Mode administrateur" : "Mode utilisateur (droits limités)";

    public bool IsDark
    {
        get => _isDark;
        private set
        {
            if (SetProperty(ref _isDark, value))
                OnPropertyChanged(nameof(ThemeIcon));
        }
    }

    public string ThemeIcon => IsDark ? "☀️" : "🌙";

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (SetProperty(ref _updateStatus, value))
                OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatus);

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingUpdate, value))
                CheckUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand RestartAsAdminCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand ShowPatchNotesCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }

    private void ToggleTheme()
    {
        bool newDark = !IsDark;
        App.SwitchTheme(newDark);
        IsDark = newDark;
    }

    private async Task CheckUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Vérification des mises à jour…";
        try
        {
            var info = await _updateService.CheckForUpdateAsync(CancellationToken.None);
            if (info is null)
            {
                UpdateStatus = string.Empty;
                _dialogs.Info("Mises à jour", "Impossible de joindre le serveur de mise à jour.");
                return;
            }

            if (!info.IsNewer)
            {
                UpdateStatus = string.Empty;
                _dialogs.Info("Mises à jour", $"CleanSlate est à jour (v{_updateService.CurrentVersion}).");
                return;
            }

            UpdateStatus = $"Mise à jour v{info.Version} disponible !";
            var download = _dialogs.Confirm("Mise à jour disponible",
                $"CleanSlate v{info.Version} est disponible (vous avez v{_updateService.CurrentVersion}).\n\n" +
                $"Notes :\n{info.ReleaseNotes}\n\nTélécharger et installer maintenant ?");

            if (!download) { UpdateStatus = string.Empty; return; }

            UpdateStatus = "Téléchargement en cours…";
            var progress = new Progress<double>(p => UpdateStatus = $"Téléchargement : {p:0}%…");
            var path = await _updateService.DownloadAsync(info, progress, CancellationToken.None);
            UpdateStatus = "Installation…";
            _updateService.LaunchInstaller(path);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = string.Empty;
            _dialogs.Warn("Erreur de mise à jour", ex.Message);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }
}
