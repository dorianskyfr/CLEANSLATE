using System.Windows.Controls;
using CleanSlate.App.ViewModels;

namespace CleanSlate.App.Views;

public partial class GameModeView : UserControl
{
    public GameModeView()
    {
        InitializeComponent();

        // Première détection des jeux lancée dès l'arrivée sur la page Mode Jeu :
        // la bibliothèque DLSS Enabler est déjà remplie quand l'onglet s'ouvre.
        Loaded += async (_, _) =>
        {
            if (DataContext is GameModeViewModel vm)
                await vm.DlssEnabler.AutoScanAsync();
        };
    }
}
