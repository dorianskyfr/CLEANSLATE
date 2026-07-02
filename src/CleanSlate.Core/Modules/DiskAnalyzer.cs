using System.Runtime.Versioning;

namespace CleanSlate.Core.Modules;

/// <summary>Un élément (dossier ou fichier) mesuré par l'analyseur d'espace disque.</summary>
public sealed record DiskUsageEntry(
    string Path,
    string Name,
    long SizeBytes,
    bool IsDirectory);

/// <summary>Résultat d'une analyse d'espace disque : les plus gros éléments d'un dossier.</summary>
public sealed record DiskUsageReport(
    string RootPath,
    long TotalScannedBytes,
    IReadOnlyList<DiskUsageEntry> TopEntries);

public interface IDiskAnalyzer
{
    /// <summary>
    /// Analyse <paramref name="rootPath"/> et renvoie ses sous-dossiers et fichiers de
    /// premier niveau, triés du plus gros au plus petit (limité à <paramref name="topN"/>).
    /// Lecture seule : rien n'est supprimé, jamais.
    /// </summary>
    Task<DiskUsageReport> AnalyzeAsync(string rootPath, int topN, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Analyseur d'espace disque (façon WinDirStat, en lecture seule) : pour un dossier
/// donné, calcule la taille de chaque sous-dossier et fichier de premier niveau et
/// renvoie les plus gros. Aide à trouver ce qui remplit le disque — CleanSlate ne
/// supprime jamais ici, il ne fait qu'informer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskAnalyzer : IDiskAnalyzer
{
    public Task<DiskUsageReport> AnalyzeAsync(string rootPath, int topN, IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return new DiskUsageReport(rootPath ?? string.Empty, 0, Array.Empty<DiskUsageEntry>());

            var entries = new List<DiskUsageEntry>();
            long total = 0;

            // Sous-dossiers de premier niveau (taille = somme récursive).
            foreach (var dir in SafeEnumerateDirectories(rootPath))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                progress?.Report($"Analyse de {name}…");
                var size = DirectorySize(dir, ct);
                total += size;
                entries.Add(new DiskUsageEntry(dir, name, size, IsDirectory: true));
            }

            // Fichiers de premier niveau.
            foreach (var file in SafeEnumerateFiles(rootPath))
            {
                ct.ThrowIfCancellationRequested();
                var size = SafeFileLength(file);
                total += size;
                entries.Add(new DiskUsageEntry(file, Path.GetFileName(file), size, IsDirectory: false));
            }

            var top = entries
                .OrderByDescending(e => e.SizeBytes)
                .Take(topN <= 0 ? entries.Count : topN)
                .ToList();

            return new DiskUsageReport(rootPath, total, top);
        }, ct);
    }

    /// <summary>Taille récursive d'un dossier, tolérante aux accès refusés (les ignore).</summary>
    internal static long DirectorySize(string path, CancellationToken ct)
    {
        long size = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            foreach (var file in SafeEnumerateFiles(current))
                size += SafeFileLength(file);

            foreach (var dir in SafeEnumerateDirectories(current))
            {
                // Ne pas suivre les points de reprise (jonctions/liens symboliques) :
                // évite les boucles et le double-comptage.
                if (!IsReparsePoint(dir))
                    stack.Push(dir);
            }
        }

        return size;
    }

    private static bool IsReparsePoint(string dir)
    {
        try { return (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static long SafeFileLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
