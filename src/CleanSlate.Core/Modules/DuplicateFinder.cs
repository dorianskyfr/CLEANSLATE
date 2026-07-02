using System.Security.Cryptography;

namespace CleanSlate.Core.Modules;

/// <summary>Un fichier membre d'un groupe de doublons.</summary>
public sealed record DuplicateFile(string Path, long SizeBytes);

/// <summary>Groupe de fichiers strictement identiques (même taille ET même empreinte SHA-256).</summary>
public sealed record DuplicateGroup(long SizeBytes, string Hash, IReadOnlyList<DuplicateFile> Files)
{
    /// <summary>Espace « gaspillé » = tout sauf un exemplaire à conserver.</summary>
    public long WastedBytes => SizeBytes * (Files.Count - 1);
}

/// <summary>Rapport de recherche de doublons (lecture seule : rien n'est supprimé).</summary>
public sealed record DuplicateReport(
    IReadOnlyList<DuplicateGroup> Groups,
    long TotalWastedBytes,
    int FilesScanned);

public interface IDuplicateFinder
{
    /// <summary>
    /// Recherche les fichiers en double sous <paramref name="rootPath"/> (taille ≥
    /// <paramref name="minSizeBytes"/>). Deux fichiers sont considérés identiques s'ils
    /// ont la même taille ET la même empreinte SHA-256. Lecture seule.
    /// </summary>
    Task<DuplicateReport> FindAsync(string rootPath, long minSizeBytes, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Détecteur de doublons en deux passes (pour rester rapide) : on regroupe d'abord par
/// TAILLE (comparaison instantanée), puis on ne calcule l'empreinte SHA-256 que pour les
/// fichiers de taille identique — inutile de hacher un fichier de taille unique. CleanSlate
/// ne fait qu'informer : la suppression reste à l'utilisateur, dans l'Explorateur.
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    public Task<DuplicateReport> FindAsync(string rootPath, long minSizeBytes, IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return new DuplicateReport(Array.Empty<DuplicateGroup>(), 0, 0);

            if (minSizeBytes < 1) minSizeBytes = 1;

            // Passe 1 : regrouper les chemins par taille de fichier.
            var bySize = new Dictionary<long, List<string>>();
            int scanned = 0;
            foreach (var file in SafeEnumerateFiles(rootPath))
            {
                ct.ThrowIfCancellationRequested();
                long size = SafeFileLength(file);
                if (size < minSizeBytes) continue;
                scanned++;
                if (!bySize.TryGetValue(size, out var list))
                    bySize[size] = list = new List<string>();
                list.Add(file);
            }

            // Passe 2 : pour chaque taille partagée par ≥ 2 fichiers, hacher et regrouper.
            var groups = new List<DuplicateGroup>();
            long totalWasted = 0;

            foreach (var (size, files) in bySize)
            {
                if (files.Count < 2) continue;
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Comparaison des fichiers de {FormatSize(size)}…");

                var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var hash = TryComputeHash(file);
                    if (hash is null) continue; // illisible : on l'ignore proprement
                    if (!byHash.TryGetValue(hash, out var hl))
                        byHash[hash] = hl = new List<string>();
                    hl.Add(file);
                }

                foreach (var (hash, sameFiles) in byHash)
                {
                    if (sameFiles.Count < 2) continue;
                    var members = sameFiles.Select(p => new DuplicateFile(p, size)).ToList();
                    var group = new DuplicateGroup(size, hash, members);
                    groups.Add(group);
                    totalWasted += group.WastedBytes;
                }
            }

            var ordered = groups.OrderByDescending(g => g.WastedBytes).ToList();
            return new DuplicateReport(ordered, totalWasted, scanned);
        }, ct);
    }

    /// <summary>Empreinte SHA-256 en hexadécimal, ou null si le fichier est illisible.</summary>
    internal static string? TryComputeHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch { return null; }
    }

    private static long SafeFileLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(current); }
            catch { subdirs = Array.Empty<string>(); }
            foreach (var d in subdirs)
            {
                // On ne suit pas les points de reprise (liens symboliques/jonctions).
                bool reparse;
                try { reparse = (new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0; }
                catch { reparse = true; }
                if (!reparse) stack.Push(d);
            }

            string[] files;
            try { files = Directory.GetFiles(current); }
            catch { files = Array.Empty<string>(); }
            foreach (var f in files) yield return f;
        }
    }

    private static string FormatSize(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}
