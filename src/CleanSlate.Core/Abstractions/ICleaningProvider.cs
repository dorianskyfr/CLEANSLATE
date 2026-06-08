using CleanSlate.Core.Models;

namespace CleanSlate.Core.Abstractions;

/// <summary>
/// Contrat de tout fournisseur de nettoyage. Le scan et le nettoyage sont
/// volontairement séparés : on liste d'abord (aperçu), on supprime ensuite
/// (après confirmation explicite de l'utilisateur).
/// </summary>
public interface ICleaningProvider
{
    /// <summary>Identifiant technique stable (utilisé pour la traçabilité/les logs).</summary>
    string Id { get; }

    /// <summary>Nom lisible affiché dans l'interface (en français).</summary>
    string DisplayName { get; }

    /// <summary>Catégorie de nettoyage couverte.</summary>
    CleaningCategory Category { get; }

    /// <summary>Niveau de prudence (pour avertir et décider du coché par défaut).</summary>
    CleaningSeverity Severity { get; }

    /// <summary>
    /// Explication honnête affichée dans l'UI : ce que ça fait, ses limites.
    /// </summary>
    string Description { get; }

    /// <summary>Indique si ce provider requiert généralement des droits administrateur.</summary>
    bool RequiresAdministrator { get; }

    /// <summary>
    /// Analyse sans rien supprimer. Retourne la liste des éléments récupérables.
    /// </summary>
    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct);

    /// <summary>
    /// Supprime les éléments fournis (issus d'un scan, validés par l'utilisateur).
    /// </summary>
    Task<CleanResult> CleanAsync(
        IReadOnlyCollection<CleanableItem> items,
        IProgress<CleanProgress>? progress,
        CancellationToken ct);
}
