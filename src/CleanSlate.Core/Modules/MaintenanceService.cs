using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Modules;

/// <summary>Une étape de l'entretien en 1 clic, avec son bilan lisible.</summary>
public sealed record MaintenanceStep(string Label, string Detail);

/// <summary>Bilan complet d'un entretien en 1 clic.</summary>
public sealed record MaintenanceReport(
    long FreedBytes,
    int DeletedCount,
    int FailedCount,
    MemoryOptimizationResult Memory,
    IReadOnlyList<MaintenanceStep> Steps);

public interface IMaintenanceService
{
    /// <summary>
    /// Entretien en 1 clic : nettoie uniquement les catégories SÛRES
    /// (<see cref="CleaningSeverity.Sur"/> — fichiers temporaires, miniatures…)
    /// puis optimise la RAM. Les actions sensibles (corbeille, cache navigateurs,
    /// Prefetch) ne sont jamais incluses : elles restent un choix explicite de
    /// l'utilisateur dans l'onglet Nettoyage.
    /// </summary>
    Task<MaintenanceReport> RunAsync(IProgress<string>? progress, CancellationToken ct);
}

public sealed class MaintenanceService : IMaintenanceService
{
    private readonly CleaningEngine _engine;
    private readonly IMemoryMonitor _memory;

    public MaintenanceService(CleaningEngine engine, IMemoryMonitor memory)
    {
        _engine = engine;
        _memory = memory;
    }

    public async Task<MaintenanceReport> RunAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var steps = new List<MaintenanceStep>();

        // 1. Scan des seuls providers sûrs (aperçu, aucune suppression).
        progress?.Report("Analyse des fichiers sûrs à nettoyer…");
        var safeIds = _engine.Providers
            .Where(p => p.Severity == CleaningSeverity.Sur)
            .Select(p => p.Id)
            .ToList();
        var scans = await _engine.ScanAllAsync(safeIds, null, ct).ConfigureAwait(false);

        // 2. Nettoyage de tout ce qui a été trouvé dans ces catégories sûres.
        var items = scans.SelectMany(s => s.Items).ToList();
        CleanResult clean;
        if (items.Count > 0)
        {
            progress?.Report($"Nettoyage de {items.Count} élément(s)…");
            clean = await _engine.CleanAsync(items, null, ct).ConfigureAwait(false);
        }
        else
        {
            clean = new CleanResult(0, 0, 0, Array.Empty<string>());
        }

        foreach (var scan in scans)
        {
            steps.Add(new MaintenanceStep(
                scan.DisplayName,
                scan.ItemCount == 0
                    ? "Rien à nettoyer."
                    : $"{scan.ItemCount} élément(s), {FormatBytes(scan.TotalSizeBytes)}."));
        }

        // 3. Optimisation RAM (Standby List si droits admin).
        progress?.Report("Optimisation de la mémoire…");
        var memory = await Task.Run(() => _memory.OptimizeMemory(clearStandbyList: true), ct)
            .ConfigureAwait(false);
        steps.Add(new MaintenanceStep("Mémoire", memory.Message));

        return new MaintenanceReport(clean.FreedBytes, clean.DeletedCount, clean.FailedCount, memory, steps);
    }

    private static string FormatBytes(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}
