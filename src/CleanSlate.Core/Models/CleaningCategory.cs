namespace CleanSlate.Core.Models;

/// <summary>
/// Catégories de nettoyage proposées par le module 1.
/// Sert à regrouper les <see cref="CleanableItem"/> et à laisser
/// l'utilisateur activer/désactiver finement chaque type de nettoyage.
/// </summary>
public enum CleaningCategory
{
    /// <summary>Fichiers temporaires utilisateur et système (%TEMP%, Windows\Temp).</summary>
    FichiersTemporaires,

    /// <summary>Cache des navigateurs (Chrome, Edge, Firefox).</summary>
    CacheNavigateurs,

    /// <summary>Journaux Windows (logs). Nécessite souvent les droits administrateur.</summary>
    LogsWindows,

    /// <summary>Corbeille Windows (vidée via l'API shell). Action irréversible.</summary>
    Corbeille,

    /// <summary>Fichiers Prefetch. ⚠️ Leur suppression n'accélère pas le PC.</summary>
    Prefetch,

    /// <summary>Cache des miniatures (thumbcache). Régénéré automatiquement.</summary>
    Miniatures,

    /// <summary>Caches de shaders DirectX/GPU (D3DSCache, NVIDIA, AMD). Régénérés au jeu.</summary>
    CacheShaders,

    /// <summary>Rapports d'erreurs Windows (WER). Données de diagnostic uniquement.</summary>
    RapportsErreurs,

    /// <summary>Cache de téléchargement de Windows Update (SoftwareDistribution). Re-téléchargé au besoin.</summary>
    CacheWindowsUpdate,

    /// <summary>Vidages mémoire de plantage (CrashDumps, Minidump). Diagnostic uniquement.</summary>
    VidagesPlantage,
}

/// <summary>
/// Niveau de prudence associé à une cible ou un élément, utilisé par l'UI
/// pour avertir l'utilisateur (et décider de ce qui est coché par défaut).
/// </summary>
public enum CleaningSeverity
{
    /// <summary>Sûr, recommandé, coché par défaut.</summary>
    Sur,

    /// <summary>Sûr mais avec une nuance (ex. cache navigateur = re-téléchargement).</summary>
    Information,

    /// <summary>À utiliser en connaissance de cause (ex. Prefetch, action irréversible).</summary>
    Avertissement,
}
