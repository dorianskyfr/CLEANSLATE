namespace CleanSlate.Core.Abstractions;

/// <summary>
/// Service de sauvegarde/restauration pour les actions RISQUÉES et réversibles
/// (au premier chef : le registre). Règle d'or du projet : aucune modification
/// de registre sans sauvegarde préalable réussie.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Crée une sauvegarde avant une action risquée. Retourne le chemin du
    /// fichier de sauvegarde (ex. export .reg). Lève si la sauvegarde échoue
    /// — auquel cas l'action risquée NE DOIT PAS être effectuée.
    /// </summary>
    Task<string> CreateBackupAsync(string scopeKey, CancellationToken ct);

    /// <summary>Restaure une sauvegarde précédemment créée.</summary>
    Task RestoreAsync(string backupFilePath, CancellationToken ct);

    /// <summary>Liste les sauvegardes disponibles (pour l'écran de restauration).</summary>
    IReadOnlyList<string> ListBackups(string scopeKey);
}
