using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Nettoyage des journaux Windows. Nécessite généralement les droits
/// administrateur, et certains fichiers (CBS.log, journaux de services actifs)
/// restent verrouillés — ils seront ignorés proprement.
/// </summary>
public sealed class WindowsLogsProvider : FileCleaningProviderBase
{
    public WindowsLogsProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "windows-logs";
    public override string DisplayName => "Journaux Windows";
    public override CleaningCategory Category => CleaningCategory.LogsWindows;
    public override CleaningSeverity Severity => CleaningSeverity.Information;
    public override bool RequiresAdministrator => true;

    public override string Description =>
        "Supprime d'anciens fichiers journaux (.log) de Windows. Nécessite les " +
        "droits administrateur ; les journaux en cours d'utilisation par un " +
        "service resteront verrouillés et seront ignorés.";

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        new CleaningTarget(@"%SystemRoot%\Logs", CleaningCategory.LogsWindows,
            recurse: true, searchPattern: "*.log"),
    };
}

/// <summary>
/// Nettoyage des fichiers Prefetch.
///
/// ⚠️ AVERTISSEMENT HONNÊTE : supprimer le Prefetch n'accélère PAS le PC. Ces
/// fichiers servent au contraire à accélérer le démarrage des applications les
/// plus utilisées. Windows les régénère ensuite. Proposé uniquement pour le gain
/// d'espace (faible), avec un avertissement et non coché par défaut côté UI.
/// </summary>
public sealed class PrefetchProvider : FileCleaningProviderBase
{
    public PrefetchProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "prefetch";
    public override string DisplayName => "Fichiers Prefetch";
    public override CleaningCategory Category => CleaningCategory.Prefetch;
    public override CleaningSeverity Severity => CleaningSeverity.Avertissement;
    public override bool RequiresAdministrator => true;

    public override string Description =>
        "⚠️ Supprime les fichiers Prefetch (.pf). À NOTER : cela n'accélère pas votre " +
        "PC — ces fichiers servent justement à accélérer le lancement des applications " +
        "et Windows les recrée. Gain d'espace faible. Décoché par défaut.";

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        new CleaningTarget(@"%SystemRoot%\Prefetch", CleaningCategory.Prefetch,
            recurse: false, searchPattern: "*.pf"),
    };
}

/// <summary>
/// Nettoyage des caches de shaders DirectX/GPU. Les jeux les régénèrent à la volée :
/// gain d'espace souvent important (plusieurs Go), au prix de micro-saccades au
/// premier lancement suivant, le temps de la recompilation. Utile aussi quand un
/// cache corrompu provoque des artefacts graphiques.
/// </summary>
public sealed class ShaderCacheProvider : FileCleaningProviderBase
{
    public ShaderCacheProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "shader-cache";
    public override string DisplayName => "Cache des shaders (DirectX/GPU)";
    public override CleaningCategory Category => CleaningCategory.CacheShaders;
    public override CleaningSeverity Severity => CleaningSeverity.Information;

    public override string Description =>
        "Supprime les caches de shaders DirectX et GPU (NVIDIA/AMD). Souvent plusieurs Go. " +
        "Les jeux les régénèrent automatiquement — attendez-vous à de légères saccades au " +
        "premier lancement suivant, le temps de la recompilation. Utile si un cache corrompu " +
        "provoque des bugs graphiques.";

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        new CleaningTarget(@"%LOCALAPPDATA%\D3DSCache", CleaningCategory.CacheShaders, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\NVIDIA\DXCache", CleaningCategory.CacheShaders, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\NVIDIA\GLCache", CleaningCategory.CacheShaders, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\AMD\DxCache", CleaningCategory.CacheShaders, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\AMD\DxcCache", CleaningCategory.CacheShaders, recurse: true),
    };
}

/// <summary>
/// Nettoyage des rapports d'erreurs Windows (WER) : données de diagnostic envoyées
/// (ou en attente d'envoi) à Microsoft après un plantage. Aucune incidence sur le
/// fonctionnement du système — peut représenter plusieurs centaines de Mo.
/// </summary>
public sealed class ErrorReportsProvider : FileCleaningProviderBase
{
    public ErrorReportsProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "error-reports";
    public override string DisplayName => "Rapports d'erreurs Windows";
    public override CleaningCategory Category => CleaningCategory.RapportsErreurs;
    public override CleaningSeverity Severity => CleaningSeverity.Information;
    public override bool RequiresAdministrator => true;

    public override string Description =>
        "Supprime les rapports d'erreurs Windows (WER) : fichiers de diagnostic créés " +
        "après les plantages d'applications. Sans effet sur le système ; droits " +
        "administrateur recommandés pour la partie commune à tous les utilisateurs.";

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        new CleaningTarget(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue",
            CleaningCategory.RapportsErreurs, recurse: true),
        new CleaningTarget(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive",
            CleaningCategory.RapportsErreurs, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue",
            CleaningCategory.RapportsErreurs, recurse: true),
        new CleaningTarget(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive",
            CleaningCategory.RapportsErreurs, recurse: true),
    };
}

/// <summary>
/// Nettoyage du cache des miniatures (thumbcache_*.db). Régénéré automatiquement
/// par l'Explorateur. Gain d'espace réel et sûr.
/// </summary>
public sealed class ThumbnailCacheProvider : FileCleaningProviderBase
{
    public ThumbnailCacheProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "thumbnails";
    public override string DisplayName => "Cache des miniatures";
    public override CleaningCategory Category => CleaningCategory.Miniatures;
    public override CleaningSeverity Severity => CleaningSeverity.Sur;

    public override string Description =>
        "Supprime le cache des miniatures de l'Explorateur. Il sera régénéré " +
        "automatiquement à l'usage. Sans risque.";

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        new CleaningTarget(
            @"%LOCALAPPDATA%\Microsoft\Windows\Explorer",
            CleaningCategory.Miniatures,
            recurse: false,
            searchPattern: "thumbcache_*.db"),
        new CleaningTarget(
            @"%LOCALAPPDATA%\Microsoft\Windows\Explorer",
            CleaningCategory.Miniatures,
            recurse: false,
            searchPattern: "iconcache_*.db"),
    };
}
