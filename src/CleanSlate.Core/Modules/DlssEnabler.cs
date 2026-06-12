using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Jeu installé détecté sur la machine (Steam, Epic ou dossier choisi à la main).
/// <paramref name="CoverImage"/> : chemin local ou URL https de la jaquette (null si inconnue).
/// </summary>
public sealed record InstalledGame(
    string Name,
    string InstallDir,
    string Source,
    string? SteamAppId = null,
    string? CoverImage = null)
{
    public override string ToString() => $"{Name} ({Source})";
}

/// <summary>État du mod DLSS Enabler dans un dossier de jeu donné.</summary>
public sealed record DlssEnablerStatus(
    bool Installed,
    IReadOnlyList<string> DetectedFiles,
    string? UninstallerPath);

/// <summary>
/// Gestion du mod « DLSS Enabler » (artur-graniszewski — github.com/artur-graniszewski/DLSS-Enabler,
/// aussi distribué sur Nexus Mods, site/mods/757). Le mod simule DLSS Super Resolution et
/// DLSS Frame Generation — y compris le Multi Frame Generation (x2/x3/x4, façon DLSS 4) —
/// sur n'importe quel GPU DirectX 12, dans les jeux qui prennent en charge DLSS2/DLSS3
/// nativement.
///
/// CleanSlate joue ici le rôle d'un gestionnaire (comme « DLSS Enabler Manager ») :
///  - détection des jeux installés (bibliothèques Steam, Epic Games, Xbox Game Pass
///    et dossiers manuels) ;
///  - détection de la présence du mod dans le dossier d'un jeu ;
///  - installation : le DLL du mod est embarqué dans CleanSlate (aucun téléchargement),
///    copié dans le dossier du jeu sous le nom de proxy le plus sûr (version.dll,
///    winmm.dll, dbghelp.dll, dxgi.dll) ou, si tous ces noms sont déjà utilisés par
///    un autre mod, sous forme de plugin ASI (plugins/dlss-enabler.asi) ;
///  - désinstallation : via le désinstallateur Inno laissé par le mod, ou à défaut
///    suppression des fichiers du mod (jamais les DLL d'autres mods, vérification
///    des métadonnées de version avant toute suppression de DLL « chargeur »).
/// </summary>
public interface IDlssEnablerService
{
    /// <summary>Version du mod DLSS Enabler embarquée dans CleanSlate.</summary>
    string EmbeddedVersion { get; }

    /// <summary>Détecte les jeux installés (Steam, Epic Games, Xbox Game Pass) + dossiers manuels.</summary>
    Task<IReadOnlyList<InstalledGame>> ScanGamesAsync(CancellationToken ct);

    /// <summary>Vérifie si DLSS Enabler est installé dans le dossier de jeu donné.</summary>
    DlssEnablerStatus GetStatus(string gameDir);

    /// <summary>
    /// Installe le mod dans le dossier du jeu à partir du DLL embarqué dans CleanSlate :
    /// copié sous le nom de proxy le plus sûr pour ce jeu (cf. <see cref="DlssEnablerStatus"/>).
    /// </summary>
    Task<bool> InstallAsync(string gameDir, CancellationToken ct);

    /// <summary>Désinstalle le mod du dossier du jeu. Renvoie false si rien n'a pu être retiré.</summary>
    Task<bool> UninstallAsync(string gameDir, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class DlssEnablerService : IDlssEnablerService
{
    /// <summary>Version du mod DLSS Enabler embarquée dans CleanSlate (DLL intégré aux ressources).</summary>
    public const string EmbeddedDllVersion = "4.7.8.1";

    /// <summary>Nom de la ressource embarquée contenant le DLL officiel de DLSS Enabler.</summary>
    private const string EmbeddedResourceName = "CleanSlate.Core.Assets.dlss-enabler.dll";

    public string EmbeddedVersion => EmbeddedDllVersion;

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
    /// Noms de proxy DLL utilisables pour injecter DLSS Enabler, par ordre de préférence
    /// (les premiers sont rarement utilisés par d'autres mods comme ReShade, qui privilégie
    /// dxgi.dll/d3d11.dll/d3d9.dll/opengl32.dll). D'autres mods peuvent utiliser les mêmes
    /// noms : leur présence ne compte comme preuve d'installation de DLSS Enabler — et n'est
    /// supprimée — que si leurs métadonnées de version le confirment.
    /// </summary>
    internal static readonly string[] LoaderFiles =
    {
        "winmm.dll", "dbghelp.dll", "version.dll", "dxgi.dll",
    };

    // ------------------------------------------------------------------
    //  Détection des jeux installés
    // ------------------------------------------------------------------

    public Task<IReadOnlyList<InstalledGame>> ScanGamesAsync(CancellationToken ct) => Task.Run(() =>
    {
        var games = new List<InstalledGame>();
        try { games.AddRange(ScanSteamGames()); } catch { }
        try { games.AddRange(ScanEpicGames()); }  catch { }
        try { games.AddRange(ScanGamePassGames()); } catch { }
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
                string? appId = null, name = null, installDir = null;
                try { (appId, name, installDir) = ParseAppManifest(File.ReadAllText(acf)); }
                catch { }
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) continue;
                if (IsSteamTooling(name)) continue;

                var dir = Path.Combine(steamApps, "common", installDir);
                if (!Directory.Exists(dir)) continue;

                // Jaquette : cache local de Steam d'abord, sinon le CDN officiel
                // (l'Image WPF télécharge l'URL de façon asynchrone).
                string? cover = null;
                if (!string.IsNullOrEmpty(appId))
                    cover = FindSteamCover(root, appId) ?? SteamCoverUrl(appId);

                yield return new InstalledGame(name, dir, "Steam", appId, cover);
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

    /// <summary>Extrait (appid, nom, dossier d'installation) d'un appmanifest_*.acf Steam.</summary>
    internal static (string? AppId, string? Name, string? InstallDir) ParseAppManifest(string acfContent)
    {
        static string? First(string content, string key)
        {
            var m = Regex.Match(content, $"\"{key}\"\\s+\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
        }
        return (First(acfContent, "appid"), First(acfContent, "name"), First(acfContent, "installdir"));
    }

    /// <summary>
    /// Cherche la jaquette verticale (600×900) d'un jeu dans le cache local de Steam.
    /// Deux dispositions existent selon la version de Steam :
    /// appcache\librarycache\&lt;appid&gt;_library_600x900.jpg (ancienne) ou
    /// appcache\librarycache\&lt;appid&gt;\library_600x900.jpg (récente).
    /// </summary>
    internal static string? FindSteamCover(string steamRoot, string appId)
    {
        try
        {
            var cache = Path.Combine(steamRoot, "appcache", "librarycache");

            foreach (var name in new[] { $"{appId}_library_600x900.jpg", $"{appId}_header.jpg" })
            {
                var p = Path.Combine(cache, name);
                if (File.Exists(p)) return p;
            }

            var subDir = Path.Combine(cache, appId);
            if (Directory.Exists(subDir))
            {
                foreach (var pattern in new[] { "library_600x900*.jpg", "header*.jpg" })
                {
                    var found = Directory.EnumerateFiles(subDir, pattern).FirstOrDefault();
                    if (found is not null) return found;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>URL de la jaquette verticale sur le CDN officiel de Steam.</summary>
    internal static string SteamCoverUrl(string appId) =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";

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

    /// <summary>
    /// Détecte les jeux installés via l'app Xbox / Xbox Game Pass : ces jeux sont posés
    /// dans un dossier « XboxGames » à la racine de chaque disque, un sous-dossier par
    /// jeu contenant lui-même un dossier « Content » avec l'exécutable.
    /// </summary>
    private static IEnumerable<InstalledGame> ScanGamePassGames()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;

            string xboxGames;
            try
            {
                if (!drive.IsReady) continue;
                xboxGames = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                if (!Directory.Exists(xboxGames)) continue;
            }
            catch { continue; }

            foreach (var dir in Directory.EnumerateDirectories(xboxGames))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) continue;

                var content = Path.Combine(dir, "Content");
                yield return new InstalledGame(name, Directory.Exists(content) ? content : dir, "Xbox Game Pass");
            }
        }
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

        // Un DLL « chargeur » (version.dll, dxgi.dll…) ne compte comme preuve d'installation
        // que si ses métadonnées de version confirment qu'il appartient à DLSS Enabler —
        // jamais celles d'un autre mod (ReShade, Special K…).
        foreach (var loader in LoaderFiles)
        {
            var path = Path.Combine(gameDir, loader);
            if (File.Exists(path) && IsDlssEnablerBinary(path))
                detected.Add(loader);
        }

        bool installed = detected.Count > 0;
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
    //  Installation / désinstallation (DLL embarqué, aucun téléchargement)
    // ------------------------------------------------------------------

    /// <summary>
    /// Installe le mod : extrait le DLL officiel embarqué dans CleanSlate et le copie
    /// dans le dossier du jeu sous le nom de proxy choisi par <see cref="ChooseProxyFileName"/>
    /// (ou en variante plugin ASI si tous les noms de proxy sont déjà pris par un autre mod).
    /// </summary>
    public async Task<bool> InstallAsync(string gameDir, CancellationToken ct)
    {
        if (!Directory.Exists(gameDir)) return false;

        byte[] dll;
        try { dll = ReadEmbeddedDll(); }
        catch { return false; }

        var proxyName = ChooseProxyFileName(gameDir);
        try
        {
            if (proxyName is not null)
            {
                await File.WriteAllBytesAsync(Path.Combine(gameDir, proxyName), dll, ct).ConfigureAwait(false);
            }
            else
            {
                // Tous les noms de proxy DLL sont déjà occupés par un autre mod : on pose
                // le mod sous forme de plugin ASI (nécessite un loader ASI, généralement
                // déjà présent dans les jeux fortement moddés qui occupent ces DLL).
                var pluginsDir = Path.Combine(gameDir, "plugins");
                Directory.CreateDirectory(pluginsDir);
                await File.WriteAllBytesAsync(Path.Combine(pluginsDir, "dlss-enabler.asi"), dll, ct).ConfigureAwait(false);
            }
        }
        catch { return false; }

        return GetStatus(gameDir).Installed;
    }

    /// <summary>Lit le DLL officiel de DLSS Enabler embarqué dans les ressources de CleanSlate.</summary>
    private static byte[] ReadEmbeddedDll()
    {
        var assembly = typeof(DlssEnablerService).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {EmbeddedResourceName}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Choisit le nom de proxy DLL à utiliser dans <paramref name="gameDir"/> :
    ///  - si DLSS Enabler y est déjà installé sous l'un des noms de <see cref="LoaderFiles"/>,
    ///    on réutilise ce nom (réinstallation / mise à jour) ;
    ///  - sinon, le premier nom libre dans <see cref="LoaderFiles"/> ;
    ///  - si tous sont déjà occupés par un autre mod (ReShade, Special K…), renvoie null
    ///    pour basculer sur la variante plugin ASI.
    /// </summary>
    internal static string? ChooseProxyFileName(string gameDir)
    {
        foreach (var loader in LoaderFiles)
        {
            var path = Path.Combine(gameDir, loader);
            if (File.Exists(path) && IsDlssEnablerBinary(path))
                return loader;
        }

        foreach (var loader in LoaderFiles)
        {
            if (!File.Exists(Path.Combine(gameDir, loader)))
                return loader;
        }

        return null;
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
}
