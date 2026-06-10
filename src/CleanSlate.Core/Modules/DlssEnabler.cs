using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CleanSlate.Core.Modules;

/// <summary>Jeu installé détecté sur la machine (Steam, Epic ou dossier choisi à la main).</summary>
public sealed record InstalledGame(string Name, string InstallDir, string Source)
{
    public override string ToString() => $"{Name} ({Source})";
}

/// <summary>État du mod DLSS Enabler dans un dossier de jeu donné.</summary>
public sealed record DlssEnablerStatus(
    bool Installed,
    IReadOnlyList<string> DetectedFiles,
    string? UninstallerPath);

/// <summary>Dernière version du mod publiée sur GitHub.</summary>
public sealed record DlssEnablerRelease(
    string Version,
    string InstallerName,
    string DownloadUrl,
    long SizeBytes);

/// <summary>
/// Gestion du mod « DLSS Enabler » (artur-graniszewski — github.com/artur-graniszewski/DLSS-Enabler,
/// aussi distribué sur Nexus Mods, site/mods/757). Le mod simule DLSS Super Resolution et
/// DLSS Frame Generation sur n'importe quel GPU DirectX 12, dans les jeux qui prennent
/// en charge DLSS2/DLSS3 nativement.
///
/// CleanSlate joue ici le rôle d'un gestionnaire (comme « DLSS Enabler Manager ») :
///  - détection des jeux installés (bibliothèques Steam et Epic Games + dossier manuel) ;
///  - détection de la présence du mod dans le dossier d'un jeu ;
///  - installation : téléchargement de l'installateur officiel depuis GitHub puis
///    exécution silencieuse (Inno Setup : /VERYSILENT /DIR="dossier du jeu") ;
///  - désinstallation : via le désinstallateur Inno laissé par le mod, ou à défaut
///    suppression des fichiers du mod (jamais les DLL d'autres mods, vérification
///    des métadonnées de version avant toute suppression de DLL « chargeur »).
/// </summary>
public interface IDlssEnablerService
{
    /// <summary>Détecte les jeux installés via Steam et Epic Games Launcher.</summary>
    Task<IReadOnlyList<InstalledGame>> ScanGamesAsync(CancellationToken ct);

    /// <summary>Vérifie si DLSS Enabler est installé dans le dossier de jeu donné.</summary>
    DlssEnablerStatus GetStatus(string gameDir);

    /// <summary>Interroge GitHub pour connaître la dernière version publiée du mod.</summary>
    Task<DlssEnablerRelease?> GetLatestReleaseAsync(CancellationToken ct);

    /// <summary>Télécharge l'installateur officiel dans un fichier temporaire.</summary>
    Task<string> DownloadInstallerAsync(DlssEnablerRelease release, IProgress<double>? progress, CancellationToken ct);

    /// <summary>Installe le mod dans le dossier du jeu (installation silencieuse).</summary>
    Task<bool> InstallAsync(string installerPath, string gameDir, CancellationToken ct);

    /// <summary>Désinstalle le mod du dossier du jeu. Renvoie false si rien n'a pu être retiré.</summary>
    Task<bool> UninstallAsync(string gameDir, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class DlssEnablerService : IDlssEnablerService
{
    private const string Owner = "artur-graniszewski";
    private const string Repo  = "DLSS-Enabler";

    /// <summary>
    /// Fichiers propres à DLSS Enabler : leur présence suffit à considérer le mod installé,
    /// et leur suppression est toujours sûre (aucun autre logiciel ne les utilise).
    /// </summary>
    internal static readonly string[] DistinctiveFiles =
    {
        "dlss-enabler.asi",
        "dlss-enabler.dll",
        "nvngx-wrapper.dll",
        "dlssg_to_fsr3_amd_is_better.dll",
        "dlss-enabler-upscaler.dll",
        "dlss-finder.exe",
    };

    /// <summary>
    /// DLL « chargeurs » génériques que l'installateur peut poser (le mod renommé en
    /// version.dll, winmm.dll…). D'autres mods (ReShade, etc.) utilisent les mêmes noms :
    /// elles ne comptent PAS comme preuve d'installation et ne sont supprimées que si
    /// leurs métadonnées de version mentionnent DLSS Enabler.
    /// </summary>
    internal static readonly string[] LoaderFiles =
    {
        "version.dll", "winmm.dll", "dbghelp.dll", "dxgi.dll",
    };

    // ------------------------------------------------------------------
    //  Détection des jeux installés
    // ------------------------------------------------------------------

    public Task<IReadOnlyList<InstalledGame>> ScanGamesAsync(CancellationToken ct) => Task.Run(() =>
    {
        var games = new List<InstalledGame>();
        try { games.AddRange(ScanSteamGames()); } catch { }
        try { games.AddRange(ScanEpicGames()); }  catch { }
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<InstalledGame> result = games
            .Where(g => Directory.Exists(g.InstallDir))
            .DistinctBy(g => g.InstallDir, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return result;
    }, ct);

    private static IEnumerable<InstalledGame> ScanSteamGames()
    {
        var root = GetSteamRoot();
        if (root is null || !Directory.Exists(root)) yield break;

        // libraryfolders.vdf liste toutes les bibliothèques Steam (y compris la principale).
        var libraries = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var steamApps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            foreach (var acf in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                string? name = null, installDir = null;
                try { (name, installDir) = ParseAppManifest(File.ReadAllText(acf)); }
                catch { }
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) continue;
                if (IsSteamTooling(name)) continue;

                var dir = Path.Combine(steamApps, "common", installDir);
                if (Directory.Exists(dir))
                    yield return new InstalledGame(name, dir, "Steam");
            }
        }
    }

    private static string? GetSteamRoot()
    {
        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (hkcu?.GetValue("SteamPath") is string p && p.Length > 0)
                return Path.GetFullPath(p.Replace('/', '\\'));
        }
        catch { }
        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (hklm?.GetValue("InstallPath") is string p && p.Length > 0)
                return p;
        }
        catch { }
        return null;
    }

    /// <summary>Outils Steam qui ne sont pas des jeux (filtrés de la liste).</summary>
    internal static bool IsSteamTooling(string name) =>
        name.Contains("Steamworks Common", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extrait les chemins de bibliothèques d'un libraryfolders.vdf (format Valve KeyValues).</summary>
    internal static IReadOnlyList<string> ParseLibraryFolders(string vdfContent)
    {
        var paths = new List<string>();
        foreach (Match m in Regex.Matches(vdfContent, "\"path\"\\s+\"([^\"]*)\""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (p.Length > 0) paths.Add(p);
        }
        return paths;
    }

    /// <summary>Extrait (nom, dossier d'installation) d'un appmanifest_*.acf Steam.</summary>
    internal static (string? Name, string? InstallDir) ParseAppManifest(string acfContent)
    {
        static string? First(string content, string key)
        {
            var m = Regex.Match(content, $"\"{key}\"\\s+\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
        }
        return (First(acfContent, "name"), First(acfContent, "installdir"));
    }

    private static IEnumerable<InstalledGame> ScanEpicGames()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? name = null, dir = null;
            try { (name, dir) = ParseEpicManifest(File.ReadAllText(file)); }
            catch { }
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir)) continue;
            if (Directory.Exists(dir))
                yield return new InstalledGame(name, dir, "Epic Games");
        }
    }

    /// <summary>Extrait (DisplayName, InstallLocation) d'un manifeste .item Epic Games.</summary>
    internal static (string? Name, string? InstallDir) ParseEpicManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
        string? dir  = root.TryGetProperty("InstallLocation", out var d) ? d.GetString() : null;
        return (name, dir);
    }

    // ------------------------------------------------------------------
    //  Détection de l'état du mod
    // ------------------------------------------------------------------

    public DlssEnablerStatus GetStatus(string gameDir)
    {
        var detected = new List<string>();
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return new DlssEnablerStatus(false, detected, null);

        foreach (var file in DistinctiveFiles)
        {
            if (File.Exists(Path.Combine(gameDir, file)))
                detected.Add(file);
        }
        // Variante « plugin ASI » : le .asi est posé dans le sous-dossier plugins.
        var asiPlugin = Path.Combine(gameDir, "plugins", "dlss-enabler.asi");
        if (File.Exists(asiPlugin))
            detected.Add(@"plugins\dlss-enabler.asi");

        bool installed = detected.Count > 0;
        if (installed)
        {
            // Les chargeurs ne sont listés que si le mod est avéré (sinon ils
            // appartiennent probablement à un autre mod : ReShade, etc.).
            foreach (var loader in LoaderFiles)
            {
                var path = Path.Combine(gameDir, loader);
                if (File.Exists(path) && IsDlssEnablerBinary(path))
                    detected.Add(loader);
            }
        }

        return new DlssEnablerStatus(installed, detected, FindUninstaller(gameDir));
    }

    /// <summary>
    /// Cherche un désinstallateur Inno Setup (unins*.exe) appartenant à DLSS Enabler
    /// (vérification des métadonnées pour ne jamais lancer celui d'un autre mod).
    /// </summary>
    private static string? FindUninstaller(string gameDir)
    {
        try
        {
            foreach (var exe in Directory.EnumerateFiles(gameDir, "unins*.exe"))
                if (IsDlssEnablerBinary(exe))
                    return exe;
        }
        catch { }
        return null;
    }

    /// <summary>Vérifie via les métadonnées de version qu'un binaire appartient bien à DLSS Enabler.</summary>
    private static bool IsDlssEnablerBinary(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return ContainsDlss(info.ProductName) || ContainsDlss(info.FileDescription)
                || ContainsDlss(info.InternalName) || ContainsDlss(info.OriginalFilename);

            static bool ContainsDlss(string? s) =>
                s is not null && s.Contains("dlss", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    //  Téléchargement / installation / désinstallation
    // ------------------------------------------------------------------

    public async Task<DlssEnablerRelease?> GetLatestReleaseAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-DlssEnabler");
        http.Timeout = TimeSpan.FromSeconds(20);

        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        var release = await http.GetFromJsonAsync<GitHubRelease>(url, ct).ConfigureAwait(false);
        if (release is null) return null;

        // L'installateur officiel s'appelle « dlss-enabler-setup-<version>.exe ».
        var asset = release.Assets?.FirstOrDefault(a =>
                a.Name?.StartsWith("dlss-enabler-setup", StringComparison.OrdinalIgnoreCase) == true &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
        if (asset?.BrowserDownloadUrl is null || asset.Name is null) return null;

        return new DlssEnablerRelease(
            Version:       release.TagName?.TrimStart('v') ?? "?",
            InstallerName: asset.Name,
            DownloadUrl:   asset.BrowserDownloadUrl,
            SizeBytes:     asset.Size);
    }

    public async Task<string> DownloadInstallerAsync(
        DlssEnablerRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        var dest = Path.Combine(Path.GetTempPath(), release.InstallerName);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-DlssEnabler");

        using var response = await http.GetAsync(
            release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? release.SizeBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(dest);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total * 100);
        }

        return dest;
    }

    public async Task<bool> InstallAsync(string installerPath, string gameDir, CancellationToken ct)
    {
        if (!File.Exists(installerPath) || !Directory.Exists(gameDir)) return false;

        // Installation silencieuse Inno Setup directement dans le dossier du jeu.
        var psi = new ProcessStartInfo(installerPath,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{gameDir}\"")
        {
            UseShellExecute = true, // l'installateur peut demander l'élévation UAC
        };
        using var process = Process.Start(psi);
        if (process is null) return false;

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode == 0 && GetStatus(gameDir).Installed;
    }

    public async Task<bool> UninstallAsync(string gameDir, CancellationToken ct)
    {
        var status = GetStatus(gameDir);
        if (!status.Installed) return false;

        // 1. Voie propre : le désinstallateur Inno laissé par le mod.
        if (status.UninstallerPath is not null)
        {
            try
            {
                var psi = new ProcessStartInfo(status.UninstallerPath,
                    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
                {
                    UseShellExecute = true,
                };
                using var process = Process.Start(psi);
                if (process is not null)
                {
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (!GetStatus(gameDir).Installed) return true;
                }
            }
            catch { /* on bascule sur la suppression manuelle */ }
        }

        // 2. Repli : suppression manuelle des fichiers du mod. Les DLL « chargeurs »
        //    (version.dll, dxgi.dll…) ne sont supprimées que si leurs métadonnées
        //    confirment qu'elles appartiennent à DLSS Enabler — jamais celles d'un
        //    autre mod (ReShade par exemple).
        bool removedAny = false;
        foreach (var file in DistinctiveFiles)
            removedAny |= TryDelete(Path.Combine(gameDir, file));
        removedAny |= TryDelete(Path.Combine(gameDir, "plugins", "dlss-enabler.asi"));

        foreach (var loader in LoaderFiles)
        {
            var path = Path.Combine(gameDir, loader);
            if (File.Exists(path) && IsDlssEnablerBinary(path))
                removedAny |= TryDelete(path);
        }

        return removedAny && !GetStatus(gameDir).Installed;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
