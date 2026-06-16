using System.Text.Json;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Une ressource téléchargeable issue d'un fichier « source » JSON fourni par
/// l'utilisateur. <paramref name="Url"/> est TOUJOURS une URL directe http(s) :
/// les liens magnet/torrent/ed2k sont rejetés à l'analyse — CleanSlate ne pilote
/// aucun client torrent et ne télécharge aucun contenu pair-à-pair.
/// </summary>
public sealed record DownloadResource(
    string Name,
    string Url,
    string? Category = null,
    string? Sha256 = null);

/// <summary>
/// Analyse un fichier « source » JSON listant des ressources à télécharger en HTTPS
/// direct. Formats acceptés (tolérant) :
///   • un tableau racine : <c>[ { ... }, { ... } ]</c>
///   • un objet avec une propriété <c>resources</c> (ou <c>items</c> / <c>downloads</c>).
/// Chaque entrée accepte les clés <c>name</c>/<c>title</c>, <c>url</c>/<c>uri</c>/<c>href</c>,
/// <c>category</c> et <c>sha256</c>.
///
/// SÉCURITÉ : seules les URLs http/https sont conservées. Toute entrée dont l'URL est un
/// magnet, un .torrent, ed2k, etc. est ignorée — par conception, pour ne pas servir de
/// pont vers un téléchargement pair-à-pair de contenus piratés.
/// </summary>
public static class DownloadSourceParser
{
    public static IReadOnlyList<DownloadResource> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<DownloadResource>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 (TryGetArray(root, "resources", out array) ||
                  TryGetArray(root, "items", out array) ||
                  TryGetArray(root, "downloads", out array)))
        {
            // array renseigné par TryGetArray
        }
        else
        {
            return Array.Empty<DownloadResource>();
        }

        var list = new List<DownloadResource>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var url  = FirstString(item, "url", "uri", "href");
            if (string.IsNullOrWhiteSpace(url) || !IsDirectHttpUrl(url)) continue;

            var name = FirstString(item, "name", "title") ?? DeriveName(url!);
            var cat  = FirstString(item, "category", "group");
            var sha  = FirstString(item, "sha256", "checksum");

            list.Add(new DownloadResource(name, url!, cat, sha));
        }
        return list;
    }

    /// <summary>Vrai uniquement pour une URL http/https absolue (refuse magnet:, ed2k:, file:, etc.).</summary>
    internal static bool IsDirectHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool TryGetArray(JsonElement obj, string prop, out JsonElement array)
    {
        if (obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            array = el;
            return true;
        }
        array = default;
        return false;
    }

    private static string? FirstString(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
            }
        return null;
    }

    private static string DeriveName(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).LocalPath);
            return string.IsNullOrWhiteSpace(name) ? url : name;
        }
        catch { return url; }
    }
}
