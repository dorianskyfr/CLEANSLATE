namespace CleanSlate.Core.Models;

/// <summary>
/// Représente un élément récupérable détecté lors d'un scan : un fichier ou un
/// dossier candidat à la suppression. Immuable : un scan produit ces éléments,
/// l'étape de nettoyage les consomme.
/// </summary>
public sealed class CleanableItem
{
    public CleanableItem(
        string path,
        long sizeBytes,
        CleaningCategory category,
        bool isDirectory,
        string providerId)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        SizeBytes = sizeBytes;
        Category = category;
        IsDirectory = isDirectory;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }

    /// <summary>Chemin absolu de l'élément sur le disque.</summary>
    public string Path { get; }

    /// <summary>Taille en octets (cumulée pour un dossier).</summary>
    public long SizeBytes { get; }

    /// <summary>Catégorie de nettoyage à laquelle appartient l'élément.</summary>
    public CleaningCategory Category { get; }

    /// <summary>Vrai s'il s'agit d'un dossier (suppression récursive).</summary>
    public bool IsDirectory { get; }

    /// <summary>Identifiant du provider qui a produit l'élément (traçabilité).</summary>
    public string ProviderId { get; }

    public override string ToString() => $"{Path} ({SizeBytes} o)";
}
