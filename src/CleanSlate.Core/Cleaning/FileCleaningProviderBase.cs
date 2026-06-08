using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Classe de base des providers de nettoyage basés sur le système de fichiers.
/// Mutualise :
///   - l'expansion des variables d'environnement,
///   - l'énumération robuste (accès refusés / fichiers verrouillés tolérés),
///   - la SÉCURITÉ : liste blanche + garde-fous anti-chemins dangereux,
///   - la suppression effective avec comptabilité des octets libérés.
///
/// Les classes dérivées n'ont qu'à fournir leurs <see cref="CleaningTarget"/>
/// et leurs métadonnées (Id, nom, description honnête…).
/// </summary>
public abstract class FileCleaningProviderBase : ICleaningProvider
{
    private readonly IActionLogger _logger;

    protected FileCleaningProviderBase(IActionLogger logger)
    {
        _logger = logger;
    }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract CleaningCategory Category { get; }
    public abstract CleaningSeverity Severity { get; }
    public abstract string Description { get; }
    public virtual bool RequiresAdministrator => false;

    /// <summary>Cibles déclarées par le provider concret.</summary>
    protected abstract IReadOnlyList<CleaningTarget> Targets { get; }

    // =====================================================================
    //  SCAN — n'efface RIEN, produit l'aperçu.
    // =====================================================================
    public virtual Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var items = new List<CleanableItem>();
            var errors = new List<string>();

            foreach (var target in Targets)
            {
                ct.ThrowIfCancellationRequested();

                var root = ExpandPath(target.RootPath);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue; // emplacement absent sur cette machine : on ignore.

                progress?.Report(new ScanProgress(DisplayName, root));

                try
                {
                    EnumerateTarget(root, target, items, errors, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors.Add($"{root} : {ex.Message}");
                }
            }

            return new ScanResult(Id, DisplayName, items, errors);
        }, ct);
    }

    private void EnumerateTarget(
        string root,
        CleaningTarget target,
        List<CleanableItem> items,
        List<string> errors,
        CancellationToken ct)
    {
        var searchOption = target.Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Énumération paresseuse + tolérante : on traite fichier par fichier pour
        // qu'un accès refusé sur l'un ne fasse pas échouer tout le scan.
        IEnumerable<string> files;
        try
        {
            files = SafeEnumerateFiles(root, target.SearchPattern, searchOption, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"{root} : {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!IsPathSafeToDelete(file, root))
                    continue;

                var info = new FileInfo(file);
                if (!info.Exists) continue;

                items.Add(new CleanableItem(file, info.Length, target.Category, isDirectory: false, Id));
            }
            catch (Exception ex)
            {
                errors.Add($"{file} : {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Énumération récursive robuste : ignore les sous-dossiers inaccessibles
    /// au lieu de lever (Directory.EnumerateFiles avec AllDirectories échoue
    /// globalement au premier accès refusé).
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(
        string root, string pattern, SearchOption option, List<string> errors)
    {
        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                results.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                errors.Add($"{dir} : {ex.Message}");
                continue;
            }

            if (option == SearchOption.AllDirectories)
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        stack.Push(sub);
                }
                catch (Exception ex)
                {
                    errors.Add($"{dir} : {ex.Message}");
                }
            }
        }
        return results;
    }

    // =====================================================================
    //  CLEAN — suppression effective, après confirmation utilisateur.
    // =====================================================================
    public virtual Task<CleanResult> CleanAsync(
        IReadOnlyCollection<CleanableItem> items,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            long freed = 0;
            int deleted = 0, failed = 0, processed = 0;
            var errors = new List<string>();
            var total = items.Count;

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                progress?.Report(new CleanProgress(processed, total, item.Path));

                // Double garde-fou : on revérifie la sécurité avant CHAQUE suppression,
                // même si l'élément vient d'un scan (défense en profondeur).
                if (!IsPathSafeToDelete(item.Path, expectedRoot: null))
                {
                    failed++;
                    errors.Add($"Refusé par sécurité : {item.Path}");
                    continue;
                }

                try
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, recursive: true);
                            freed += item.SizeBytes;
                            deleted++;
                        }
                    }
                    else if (File.Exists(item.Path))
                    {
                        // On retire l'attribut lecture seule au besoin.
                        var attrs = File.GetAttributes(item.Path);
                        if (attrs.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(item.Path, attrs & ~FileAttributes.ReadOnly);

                        File.Delete(item.Path);
                        freed += item.SizeBytes;
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    // Fichier verrouillé / droits insuffisants : on n'insiste pas,
                    // on ne force jamais. C'est un échec attendu, pas un crash.
                    failed++;
                    errors.Add($"{item.Path} : {ex.Message}");
                }
            }

            _logger.LogCleaning(Id, deleted, failed, freed);
            return new CleanResult(freed, deleted, failed, errors);
        }, ct);
    }

    // =====================================================================
    //  SÉCURITÉ — cœur de la confiance dans l'outil.
    // =====================================================================

    /// <summary>
    /// Liste de fragments de chemins JAMAIS supprimables, quelle que soit la cible.
    /// Garde-fou ultime contre une cible mal déclarée.
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    {
        @"\windows\system32",
        @"\windows\syswow64",
        @"\program files",
        @"\program files (x86)",
    };

    /// <summary>
    /// Détermine si un chemin est sûr à supprimer. Règles :
    ///   1. Chemin absolu, enraciné.
    ///   2. Pas une racine de disque (C:\, D:\…).
    ///   3. Pas le dossier profil utilisateur lui-même ni un dossier système.
    ///   4. Si <paramref name="expectedRoot"/> est fourni, le chemin DOIT être
    ///      contenu dedans (empêche toute sortie de la zone déclarée).
    ///   5. Ne contient aucun fragment interdit.
    /// </summary>
    protected static bool IsPathSafeToDelete(string path, string? expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }

        // 2. Refuse une racine de disque.
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd('\\', '/'), root?.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
            return false;

        // 3. Refuse le dossier profil utilisateur lui-même.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) &&
            string.Equals(full.TrimEnd('\\'), profile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return false;

        var lower = full.ToLowerInvariant();

        // 5. Refuse les fragments interdits.
        foreach (var frag in ForbiddenFragments)
            if (lower.Contains(frag))
                return false;

        // 4. Confinement à la racine déclarée si fournie.
        if (!string.IsNullOrEmpty(expectedRoot))
        {
            var fullRoot = Path.GetFullPath(expectedRoot)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>Expanse les variables d'environnement d'un chemin déclaré.</summary>
    protected static string ExpandPath(string path) =>
        Environment.ExpandEnvironmentVariables(path);

    protected IActionLogger Logger => _logger;
}
