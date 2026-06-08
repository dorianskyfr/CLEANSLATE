using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Déclaration d'une cible de nettoyage : un emplacement à analyser, avec ses
/// options. Approche déclarative : ajouter une cible = ajouter une instance,
/// sans toucher à la logique d'énumération/suppression.
/// </summary>
public sealed class CleaningTarget
{
    public CleaningTarget(
        string rootPath,
        CleaningCategory category,
        bool recurse = true,
        string searchPattern = "*",
        bool deleteRootFolder = false)
    {
        RootPath = rootPath;
        Category = category;
        Recurse = recurse;
        SearchPattern = searchPattern;
        DeleteRootFolder = deleteRootFolder;
    }

    /// <summary>
    /// Chemin racine, pouvant contenir des variables d'environnement
    /// (ex. "%TEMP%", "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache").
    /// </summary>
    public string RootPath { get; }

    public CleaningCategory Category { get; }

    /// <summary>Descendre récursivement dans les sous-dossiers.</summary>
    public bool Recurse { get; }

    /// <summary>Filtre de fichiers (ex. "*.log", "thumbcache_*.db").</summary>
    public string SearchPattern { get; }

    /// <summary>
    /// Si vrai, le dossier racine lui-même peut être supprimé (rare). Par défaut on
    /// supprime uniquement le contenu, jamais le dossier connu lui-même.
    /// </summary>
    public bool DeleteRootFolder { get; }
}
