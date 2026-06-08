using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanSlate.App.Infrastructure;

/// <summary>
/// Base minimale pour le pattern MVVM : notification de changement de propriété.
/// (On évite une dépendance externe pour rester léger ; CommunityToolkit.Mvvm
/// serait une alternative naturelle pour un projet plus gros.)
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Affecte une valeur et notifie si elle a changé. Retourne true si changée.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
