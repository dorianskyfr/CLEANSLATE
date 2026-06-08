using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Élément de la barre de navigation latérale : un titre, une icône (emoji pour
/// rester sans dépendance de police d'icônes) et le ViewModel de la page associée.
/// </summary>
public sealed class NavigationItem : ObservableObject
{
    public NavigationItem(string title, string glyph, ObservableObject viewModel)
    {
        Title = title;
        Glyph = glyph;
        ViewModel = viewModel;
    }

    public string Title { get; }
    public string Glyph { get; }
    public ObservableObject ViewModel { get; }
}
