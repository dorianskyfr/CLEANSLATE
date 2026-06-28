using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace CleanSlate.Core.Modules;

/// <summary>Résultat de la vérification du dernier pilote disponible chez le fabricant.</summary>
public sealed record GpuDriverCheckResult(
    bool Success,
    string Message,
    string? InstalledVersionDisplay,
    string? LatestVersion,
    string? LatestReleaseDate,
    string? DownloadUrl,
    long? DownloadSizeBytes,
    bool UpdateAvailable)
{
    public string DownloadSizeDisplay
    {
        get
        {
            if (DownloadSizeBytes is not > 0) return string.Empty;
            double mb = DownloadSizeBytes.Value / 1024d / 1024d;
            return mb >= 1024 ? $"{mb / 1024:0.#} Go" : $"{mb:0} Mo";
        }
    }
}

/// <summary>
/// Vérifie auprès du fabricant (NVIDIA / AMD / Intel) la dernière version de pilote
/// disponible pour la carte graphique détectée — Windows Update peut être en retard
/// de plusieurs semaines, voire mois, sur les pilotes « Game Ready »/Adrenalin.
///
/// NVIDIA : interrogation directe de l'API officielle de recherche de pilotes
/// (catalogue produits + service de recherche), sans inscription ni clé.
/// AMD/Intel : pas d'API publique fiable par modèle — les pilotes sont unifiés
/// par génération, donc on redirige vers l'outil de détection officiel.
/// </summary>
public interface IGpuDriverChecker
{
    Task<GpuDriverCheckResult> CheckLatestAsync(GpuInfo gpu, CancellationToken ct);
}

public sealed class GpuDriverChecker : IGpuDriverChecker
{
    private const string NvidiaFamilyListUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
    private const string NvidiaSearchPage    = "https://www.nvidia.com/Download/index.aspx";
    private const string AmdSupportPage      = "https://www.amd.com/en/support";
    private const string IntelDetectPage     = "https://www.intel.com/content/www/us/en/support/detect.html";

    // HttpClient partagé (évite l'épuisement des sockets entre vérifications répétées).
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-DriverCheck");
        return http;
    }

    public async Task<GpuDriverCheckResult> CheckLatestAsync(GpuInfo gpu, CancellationToken ct)
    {
        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => await CheckNvidiaAsync(gpu, ct),
            GpuVendor.Amd    => CheckAmd(gpu),
            GpuVendor.Intel  => CheckIntel(gpu),
            _ => new GpuDriverCheckResult(false,
                "Marque non reconnue : impossible de vérifier les pilotes auprès du fabricant.",
                gpu.DriverVersion, null, null, null, null, false),
        };
    }

    private static async Task<GpuDriverCheckResult> CheckNvidiaAsync(GpuInfo gpu, CancellationToken ct)
    {
        var installedDisplay = ConvertNvidiaVersion(gpu.DriverVersion) ?? gpu.DriverVersion;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeoutCts.Token;

            var xml = await Http.GetStringAsync(NvidiaFamilyListUrl, token);
            var doc = XDocument.Parse(xml);
            var families = doc.Descendants("LookupValue")
                .Select(e => new
                {
                    Name = ((string?)e.Element("Name"))?.Trim() ?? "",
                    Pfid = ((string?)e.Element("Value"))?.Trim() ?? "",
                    Psid = ((string?)e.Attribute("ParentID"))?.Trim() ?? "",
                })
                .Where(e => e.Name.Length > 0 && e.Pfid.Length > 0 && e.Psid.Length > 0)
                .ToList();

            var normalizedGpuName = Normalize(gpu.Name);
            var match = families.FirstOrDefault(f => Normalize(f.Name) == normalizedGpuName);

            if (match is null)
                return new GpuDriverCheckResult(false,
                    $"Impossible d'identifier précisément « {gpu.Name} » dans le catalogue NVIDIA. " +
                    "Utilisez la recherche manuelle sur le site NVIDIA.",
                    installedDisplay, null, null, NvidiaSearchPage, null, false);

            var osId = Environment.OSVersion.Version.Build >= 22000 ? 135 : 57; // Windows 11 / Windows 10 64-bit
            var url = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
                      $"?func=DriverManualLookup&psid={match.Psid}&pfid={match.Pfid}&osID={osId}" +
                      "&languageCode=1033&beta=null&isWHQL=1&dltype=-1&dch=1&upCRD=null&qnf=0&sort1=0&numberOfResults=1";

            var json = await Http.GetStringAsync(url, token);
            var response = JsonSerializer.Deserialize<NvidiaLookupResponse>(json);
            var info = response?.IDS?.FirstOrDefault()?.DownloadInfo;

            if (info?.Version is null)
                return new GpuDriverCheckResult(false,
                    "NVIDIA n'a renvoyé aucun pilote pour ce modèle pour le moment.",
                    installedDisplay, null, null, NvidiaSearchPage, null, false);

            var size = ParseSizeToBytes(info.DownloadURLFileSize);
            var updateAvailable = IsNvidiaNewer(installedDisplay, info.Version);

            var message = updateAvailable
                ? $"Une nouvelle version est disponible chez NVIDIA : {info.Version} (vous avez {installedDisplay ?? "version inconnue"})."
                : $"Vous avez déjà la dernière version disponible chez NVIDIA ({info.Version}).";

            return new GpuDriverCheckResult(true, message, installedDisplay,
                info.Version, info.ReleaseDateTime, info.DownloadURL, size, updateAvailable);
        }
        catch (Exception ex)
        {
            return new GpuDriverCheckResult(false,
                $"Impossible de contacter les serveurs NVIDIA : {ex.Message}",
                installedDisplay, null, null, NvidiaSearchPage, null, false);
        }
    }

    private static GpuDriverCheckResult CheckAmd(GpuInfo gpu) => new(
        true,
        "Les pilotes AMD Radeon (« AMD Software : Adrenalin Edition ») sont unifiés par génération : " +
        "il n'existe pas de version « par modèle » à comparer. Le détecteur officiel AMD identifie " +
        "votre carte et propose toujours la dernière version disponible.",
        gpu.DriverVersion, null, null, AmdSupportPage, null, false);

    private static GpuDriverCheckResult CheckIntel(GpuInfo gpu) => new(
        true,
        "Pour les GPU Intel, l'outil officiel « Intel Driver & Support Assistant » détecte " +
        "automatiquement votre matériel et propose toujours la dernière version disponible.",
        gpu.DriverVersion, null, null, IntelDetectPage, null, false);

    private static string Normalize(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();

    /// <summary>
    /// Convertit le format de version WMI (ex. "32.0.15.6614") au format affiché par
    /// NVIDIA (ex. "566.14") : dernier chiffre du 3ᵉ segment, suivi du 4ᵉ segment
    /// avec un point inséré avant les 2 derniers chiffres.
    /// </summary>
    internal static string? ConvertNvidiaVersion(string? wmiVersion)
    {
        if (string.IsNullOrWhiteSpace(wmiVersion)) return null;
        var parts = wmiVersion.Split('.');
        if (parts.Length != 4 || parts[2].Length == 0) return null;

        var fourth = parts[3].PadLeft(4, '0');
        if (fourth.Length < 3) return null;

        var major = $"{parts[2][^1]}{fourth[..^2]}";
        var minor = fourth[^2..];
        return $"{major}.{minor}";
    }

    /// <summary>
    /// Vrai si le pilote NVIDIA <paramref name="latestVersion"/> est plus récent que
    /// <paramref name="installedDisplay"/>. Les versions NVIDIA (« 566.14 ») NE sont
    /// PAS des décimales : « 566.14 » est plus récent que « 566.9 ». On compare donc
    /// (majeur, mineur) en entiers, et non en double (qui ordonnait 566.14 &lt; 566.9).
    /// </summary>
    internal static bool IsNvidiaNewer(string? installedDisplay, string latestVersion)
    {
        var latest = ParseNvidiaVersion(latestVersion);
        if (latest is null) return false;
        var installed = ParseNvidiaVersion(installedDisplay);
        if (installed is null) return true; // version installée inconnue : on signale la dernière dispo par sécurité
        return latest.Value.CompareTo(installed.Value) > 0;
    }

    /// <summary>« 566.14 » → (566, 14). Renvoie null si le majeur n'est pas numérique.</summary>
    private static (int Major, int Minor)? ParseNvidiaVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Trim().Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
            return null;
        int minor = 0;
        if (parts.Length > 1)
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor);
        return (major, minor);
    }

    internal static long? ParseSizeToBytes(string? sizeText)
    {
        if (string.IsNullOrWhiteSpace(sizeText)) return null;
        var cleaned = sizeText.Replace("MB", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("GB", "", StringComparison.OrdinalIgnoreCase)
                               .Trim();
        if (!double.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;

        return sizeText.Contains("GB", StringComparison.OrdinalIgnoreCase)
            ? (long)(value * 1024 * 1024 * 1024)
            : (long)(value * 1024 * 1024);
    }

    private sealed class NvidiaLookupResponse
    {
        [JsonPropertyName("IDS")] public List<NvidiaIdsEntry>? IDS { get; set; }
    }

    private sealed class NvidiaIdsEntry
    {
        [JsonPropertyName("downloadInfo")] public NvidiaDownloadInfo? DownloadInfo { get; set; }
    }

    private sealed class NvidiaDownloadInfo
    {
        [JsonPropertyName("Version")] public string? Version { get; set; }
        [JsonPropertyName("ReleaseDateTime")] public string? ReleaseDateTime { get; set; }
        [JsonPropertyName("DownloadURL")] public string? DownloadURL { get; set; }
        [JsonPropertyName("DownloadURLFileSize")] public string? DownloadURLFileSize { get; set; }
    }
}
