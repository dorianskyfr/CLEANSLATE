using System.Collections.ObjectModel;
using System.Windows.Input;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string PatchNotes =
        "Version 0.1 (2025-06)\n\n" +
        "• Nettoyage : fichiers temp, cache navigateurs, miniatures, corbeille, journaux Windows, prefetch\n" +
        "• Mémoire : surveillance en temps réel + optimisation avancée (Standby List)\n" +
        "• Pilotes : inventaire WMI + recherche/installation de mises à jour via Windows Update (WUApi)\n" +
        "• Mode Jeu : suspension des processus non essentiels (Discord conservé)\n" +
        "• Optimisation : gestionnaire démarrage + nettoyage registre\n" +
        "• Réparation rapide : diagnostic système en 6 points\n" +
        "• Thème sombre / clair basculable à chaud\n" +
        "• Mise à jour automatique via GitHub Releases";

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
