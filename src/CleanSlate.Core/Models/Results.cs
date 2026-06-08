using System.Collections.ObjectModel;

namespace CleanSlate.Core.Models;

/// <summary>
/// Résultat d'une opération de scan (aperçu). Ne supprime rien : il liste ce qui
/// pourrait l'être. C'est le cœur de l'exigence « aperçu avant suppression ».
/// </summary>
public sealed class ScanResult
{
    public ScanResult(
        string providerId,
        string displayName,
        IReadOnlyList<CleanableItem> items,
        IReadOnlyList<string> errors)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        Items = items;
        Errors = errors;
    }

    public string ProviderId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<CleanableItem> Items { get; }

    /// <summary>Erreurs non bloquantes (accès refusé, fichier verrouillé…).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Taille totale récupérable, en octets.</summary>
    public long TotalSizeBytes => Items.Sum(i => i.SizeBytes);

    public int ItemCount => Items.Count;

    public static ScanResult Empty(string providerId, string displayName) =>
        new(providerId, displayName, Array.Empty<CleanableItem>(), Array.Empty<string>());
}

/// <summary>
/// Résultat d'une opération de nettoyage effective (après confirmation).
/// </summary>
public sealed class CleanResult
{
    public CleanResult(
        long freedBytes,
        int deletedCount,
        int failedCount,
        IReadOnlyList<string> errors)
    {
        FreedBytes = freedBytes;
        DeletedCount = deletedCount;
        FailedCount = failedCount;
        Errors = errors;
    }

    /// <summary>Octets effectivement libérés.</summary>
    public long FreedBytes { get; }

    /// <summary>Nombre d'éléments supprimés avec succès.</summary>
    public int DeletedCount { get; }

    /// <summary>Nombre d'éléments non supprimés (verrouillés, droits…).</summary>
    public int FailedCount { get; }

    public IReadOnlyList<string> Errors { get; }

    public static CleanResult Combine(IEnumerable<CleanResult> results)
    {
        long freed = 0;
        int deleted = 0, failed = 0;
        var errors = new Collection<string>();
        foreach (var r in results)
        {
            freed += r.FreedBytes;
            deleted += r.DeletedCount;
            failed += r.FailedCount;
            foreach (var e in r.Errors) errors.Add(e);
        }
        return new CleanResult(freed, deleted, failed, errors);
    }
}

/// <summary>Progression d'un scan (pour barre de progression / statut UI).</summary>
public readonly record struct ScanProgress(string ProviderDisplayName, string CurrentPath);

/// <summary>Progression d'un nettoyage.</summary>
public readonly record struct CleanProgress(int Processed, int Total, string CurrentPath);
