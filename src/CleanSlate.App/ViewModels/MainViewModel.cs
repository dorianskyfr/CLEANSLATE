using System.Windows.Input;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// ViewModel racine : porte l'état global (titre, statut administrateur) et la
/// navigation. Pour l'instant, seul le module 1 (nettoyage) est branché ;
/// les autres onglets seront ajoutés au même endroit.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public MainViewModel(CleaningViewModel cleaning, IDialogService dialogs)
    {
        Cleaning = cleaning;
        IsAdministrator = ElevationHelper.IsRunningAsAdministrator();

        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (!ElevationHelper.RestartAsAdministrator())
                dialogs.Warn("Élévation refusée",
                    "L'application continue sans droits administrateur. " +
                    "Certaines cibles système ne pourront pas être nettoyées.");
            else
                System.Windows.Application.Current.Shutdown();
        }, () => !IsAdministrator);
    }

    public CleaningViewModel Cleaning { get; }

    /// <summary>Vrai si l'application tourne avec les droits administrateur.</summary>
    public bool IsAdministrator { get; }

    /// <summary>Afficher le bouton d'élévation uniquement si on n'est PAS admin.</summary>
    public bool ShowElevationButton => !IsAdministrator;

    /// <summary>Texte d'état affiché dans la barre supérieure.</summary>
    public string PrivilegeStatus => IsAdministrator
        ? "Mode administrateur"
        : "Mode utilisateur (droits limités)";

    public ICommand RestartAsAdminCommand { get; }
}
