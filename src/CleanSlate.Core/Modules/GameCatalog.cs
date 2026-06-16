using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Un jeu du catalogue mondial (liste officielle des applications Steam). Sert à
/// afficher « tous les jeux du monde » dans la bibliothèque, avec jaquette et lien
/// vers la page officielle du magasin. <paramref name="AppId"/> est l'identifiant Steam.
/// </summary>
public sealed record CatalogGame(int AppId, string Name)
{
    /// <summary>Jaquette verticale 600×900 sur le CDN officiel de Steam.</summary>
    public string CoverUrl =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{AppId}/library_600x900.jpg";

    /// <summary>Page officielle du jeu sur le magasin Steam (achat / téléchargement légal).</summary>
    public string StoreUrl => $"https://store.steampowered.com/app/{AppId}/";
}

/// <summary>
/// Catalogue de jeux « façon Hydra » mais 100 % légal : il s'appuie sur la liste
/// officielle et publique des applications Steam (ISteamApps/GetAppList) pour permettre
/// de rechercher parmi tous les jeux du monde et d'ouvrir leur page officielle du
/// magasin. Aucun téléchargement de jeu (et a fortiori aucun lien torrent/repack) n'est
/// géré ici : CleanSlate n'aide pas à pirater des jeux.
/// </summary>
public interface IGameCatalogService
{
    /// <summary>
    /// Recherche jusqu'à <paramref name="maxResults"/> jeux dont le nom contient
    /// <paramref name="query"/>. La liste complète est téléchargée une fois puis mise en
    /// cache (mémoire + disque). Renvoie une liste vide en cas d'échec réseau, sans exception.
    /// </summary>
    Task<IReadOnlyList<CatalogGame>> SearchAsync(string query, int maxResults, CancellationToken ct);
}

public sealed class SteamGameCatalogService : IGameCatalogService
{
    private const string AppListUrl = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);

    private readonly string _cacheFile;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IReadOnlyList<CatalogGame>? _catalog;

    public SteamGameCatalogService(string? cacheFile = null)
    {
        _cacheFile = cacheFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "steam-applist.json");
    }

    public async Task<IReadOnlyList<CatalogGame>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<CatalogGame>();

        var catalog = await EnsureCatalogAsync(ct).ConfigureAwait(false);
        if (catalog.Count == 0) return Array.Empty<CatalogGame>();

        var term = query.Trim();

        // Pertinence : un nom qui COMMENCE par le terme passe avant un simple « contient ».
        var matches = catalog
            .Where(g => g.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(g => g.Name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(g => g.Name.Length)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .ToList();

        return matches;
    }

    private async Task<IReadOnlyList<CatalogGame>> EnsureCatalogAsync(CancellationToken ct)
    {
        if (_catalog is not null) return _catalog;

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_catalog is not null) return _catalog;

            var fromCache = TryLoadCache();
            if (fromCache is not null)
            {
                _catalog = fromCache;
                return _catalog;
            }

            var fromNetwork = await DownloadAsync(ct).ConfigureAwait(false);
            _catalog = fromNetwork ?? Array.Empty<CatalogGame>();
            if (fromNetwork is { Count: > 0 }) TrySaveCache(fromNetwork);
            return _catalog;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<IReadOnlyList<CatalogGame>?> DownloadAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-Catalog");

            var payload = await http.GetFromJsonAsync<AppListPayload>(AppListUrl, ct).ConfigureAwait(false);
            return Normalize(payload);
        }
        catch { return null; }
    }

    /// <summary>Filtre/dédoublonne la liste brute de Steam (retire les entrées sans nom et le bruit évident).</summary>
    internal static IReadOnlyList<CatalogGame> Normalize(AppListPayload? payload)
    {
        var apps = payload?.AppList?.Apps;
        if (apps is null) return Array.Empty<CatalogGame>();

        var seen = new HashSet<int>();
        var list = new List<CatalogGame>(apps.Count);
        foreach (var a in apps)
        {
            if (a.AppId <= 0 || string.IsNullOrWhiteSpace(a.Name)) continue;
            if (IsNonGameNoise(a.Name)) continue;
            if (!seen.Add(a.AppId)) continue;
            list.Add(new CatalogGame(a.AppId, a.Name.Trim()));
        }
        return list;
    }

    /// <summary>
    /// Écarte le bruit le plus évident de la liste Steam (serveurs dédiés, kits SDK,
    /// bandes-son, démos…) pour que la recherche renvoie surtout de vrais jeux. Léger
    /// volontairement : on ne veut pas masquer de vrais titres.
    /// </summary>
    internal static bool IsNonGameNoise(string name)
    {
        string[] noise =
        {
            "dedicated server", "sdk", "soundtrack", "- ost", "benchmark",
            "trailer", "teaser", "playtest", "beta test",
        };
        return noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<CatalogGame>? TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFile)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFile) > CacheMaxAge) return null;

            var payload = JsonSerializer.Deserialize<AppListPayload>(File.ReadAllText(_cacheFile));
            var list = Normalize(payload);
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    private void TrySaveCache(IReadOnlyList<CatalogGame> games)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // On re-sérialise au format Steam (applist/apps) pour pouvoir relire via Normalize.
            var payload = new AppListPayload
            {
                AppList = new AppListContainer
                {
                    Apps = games.Select(g => new AppEntry { AppId = g.AppId, Name = g.Name }).ToList(),
                },
            };
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(payload));
        }
        catch { /* le cache est un confort, pas une garantie */ }
    }

    // ---- Modèle JSON de ISteamApps/GetAppList/v2 ----

    internal sealed class AppListPayload
    {
        [JsonPropertyName("applist")] public AppListContainer? AppList { get; set; }
    }

    internal sealed class AppListContainer
    {
        [JsonPropertyName("apps")] public List<AppEntry>? Apps { get; set; }
    }

    internal sealed class AppEntry
    {
        [JsonPropertyName("appid")] public int AppId { get; set; }
        [JsonPropertyName("name")]  public string? Name { get; set; }
    }
}
