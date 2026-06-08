using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Orchestrateur du module 1. Coordonne plusieurs <see cref="ICleaningProvider"/>
/// pour le scan et le nettoyage. Sépare strictement les deux phases :
///   - <see cref="ScanAllAsync"/> ne supprime RIEN (aperçu),
///   - <see cref="CleanAsync"/> agit uniquement sur des éléments fournis,
///     préalablement listés et confirmés par l'utilisateur.
/// </summary>
public sealed class CleaningEngine
{
    private readonly IReadOnlyList<ICleaningProvider> _providers;
    private readonly IActionLogger _logger;

    public CleaningEngine(IEnumerable<ICleaningProvider> providers, IActionLogger logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <summary>Liste des providers disponibles (pour peupler l'UI).</summary>
    public IReadOnlyList<ICleaningProvider> Providers => _providers;

    /// <summary>
    /// Analyse l'ensemble des providers demandés. Retourne un résultat par provider.
    /// Aucune suppression.
    /// </summary>
    public async Task<IReadOnlyList<ScanResult>> ScanAllAsync(
        IEnumerable<string> providerIds,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var wanted = providerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ScanResult>();

        foreach (var provider in _providers.Where(p => wanted.Contains(p.Id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await provider.ScanAsync(progress, ct).ConfigureAwait(false);
                results.Add(result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error($"Scan échoué pour {provider.Id}", ex);
                results.Add(new ScanResult(provider.Id, provider.DisplayName,
                    Array.Empty<CleanableItem>(), new[] { ex.Message }));
            }
        }

        return results;
    }

    /// <summary>
    /// Nettoie les éléments confirmés, regroupés par provider d'origine.
    /// </summary>
    public async Task<CleanResult> CleanAsync(
        IReadOnlyCollection<CleanableItem> confirmedItems,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        var results = new List<CleanResult>();

        // Regroupe par provider pour appeler chaque implémentation appropriée
        // (ex. la corbeille a sa propre logique d'effacement).
        foreach (var group in confirmedItems.GroupBy(i => i.ProviderId))
        {
            ct.ThrowIfCancellationRequested();
            var provider = _providers.FirstOrDefault(p => p.Id == group.Key);
            if (provider is null)
            {
                _logger.Warning($"Provider inconnu ignoré : {group.Key}");
                continue;
            }

            var result = await provider.CleanAsync(group.ToList(), progress, ct)
                .ConfigureAwait(false);
            results.Add(result);
        }

        return CleanResult.Combine(results);
    }
}
