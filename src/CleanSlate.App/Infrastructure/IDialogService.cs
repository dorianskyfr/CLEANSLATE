namespace CleanSlate.App.Infrastructure;

/// <summary>
/// Abstraction des boîtes de dialogue, pour que les ViewModels n'aient pas de
/// dépendance directe à WPF (MessageBox) et restent testables.
/// </summary>
public interface IDialogService
{
    /// <summary>Demande une confirmation (Oui/Non). Retourne true si l'utilisateur confirme.</summary>
    bool Confirm(string title, string message);

    void Info(string title, string message);
    void Warn(string title, string message);

    /// <summary>Ouvre un sélecteur de dossier. Renvoie null si l'utilisateur annule.</summary>
    string? PickFolder(string title);

    /// <summary>
    /// Ouvre un sélecteur de fichier. <paramref name="filter"/> suit la syntaxe Win32
    /// (« JSON|*.json|Tous|*.* »). Renvoie null si l'utilisateur annule.
    /// </summary>
    string? PickFile(string title, string filter);
}
