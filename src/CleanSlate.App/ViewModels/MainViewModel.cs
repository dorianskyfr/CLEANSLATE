using System.Collections.ObjectModel;
using System.Windows.Input;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// ViewModel racine : porte l'état global (statut administrateur), la navigation
/// latérale entre les modules, et la page courante affichée.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private NavigationItem _selectedItem = null!;

    public MainViewModel(
        CleaningViewModel cleaning,
        MemoryViewModel memory,
        DriversViewModel drivers,
        GameModeViewModel gameMode,
        OptimizationViewModel optimization,
        IDialogService dialogs)
    {
        IsAdministrator = ElevationHelper.IsRunningAsAdministrator();

        // Construction de la navigation. Icônes en emoji pour éviter une dépendance
        // à une police d'icônes externe.
        Items = new ObservableCollection<NavigationItem>
        {
            new("Nettoyage", "🧹", cleaning),
            new("Mémoire", "📊", memory),
            new("Pilotes", "🧩", drivers),
            new("Mode Jeu", "🎮", gameMode),
            new("Optimisation", "⚙️", optimization),
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
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    /// <summary>Vrai si l'application tourne avec les droits administrateur.</summary>
    public bool IsAdministrator { get; }

    /// <summary>Afficher le bouton d'élévation uniquement si on n'est PAS admin.</summary>
    public bool ShowElevationButton => !IsAdministrator;

    public string PrivilegeStatus => IsAdministrator
        ? "Mode administrateur"
        : "Mode utilisateur (droits limités)";

    public ICommand RestartAsAdminCommand { get; }
}
