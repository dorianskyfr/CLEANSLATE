using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>Raison d'échec d'une installation du mod (pour un message UI adapté).</summary>
public enum DlssInstallFailure
{
    None,

    /// <summary>Le dossier du jeu n'existe pas/plus.</summary>
    GameDirMissing,

    /// <summary>
    /// Écriture refusée dans le dossier du jeu — typique des jeux Xbox Game Pass, dont
    /// le dossier est verrouillé par Windows tant que les options de modding ne sont pas
    /// activées dans l'app Xbox (« Activer les fonctionnalités de modding avancées »).
    /// </summary>
    WriteDenied,

    /// <summary>Erreur inattendue (fichier verrouillé par le jeu en cours, disque plein…).</summary>
    Unknown,
}

/// <summary>
/// Résultat d'une installation du mod : sous quel nom et dans quel dossier le DLL
/// a été posé (le dossier de l'EXÉCUTABLE du jeu, pas forcément la racine).
/// </summary>
public sealed record DlssInstallResult(
    bool Success,
    string? InstalledFile,
    string? TargetDir,
    DlssInstallFailure Failure = DlssInstallFailure.None);

/// <summary>Niveau de compatibilité d'un jeu avec DLSS Enabler.</summary>
public enum DlssCompatibility
{
    /// <summary>DLSS natif détecté (nvngx_dlss / Streamline) : le mod fonctionnera.</summary>
    Compatible,

    /// <summary>Upscaler détecté (FSR / XeSS) mais pas DLSS : le mod peut aider, sans garantie.</summary>
    Maybe,

    /// <summary>Aucun upscaler détecté : le jeu ne supporte probablement pas DLSS — le mod n'aura aucun effet.</summary>
    Unlikely,
}

/// <summary>
/// Diagnostic de compatibilité d'un jeu : DLSS Enabler ne fait qu'« activer » le DLSS
/// (et la Frame Generation) sur des jeux qui embarquent DÉJÀ les composants DLSS/FSR3/XeSS.
/// Il ne crée pas le support DLSS à partir de rien : sur un jeu sans aucun upscaler, il
/// n'a aucun effet. <paramref name="Evidence"/> liste les fichiers révélateurs trouvés.
/// </summary>
public sealed record DlssCompatibilityInfo(
    DlssCompatibility Level,
    IReadOnlyList<string> Evidence,
    string Summary);

/// <summary>
/// Gestion du mod « DLSS Enabler » (artur-graniszewski — github.com/artur-graniszewski/DLSS-Enabler,
/// aussi distribué sur Nexus Mods, site/mods/757). Le mod simule DLSS Super Resolution et
/// DLSS Frame Generation — y compris le Multi Frame Generation (x2 à x6, façon DLSS 4.5) —
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
    /// Évalue si le jeu supporte DLSS (donc si le mod peut faire quelque chose) en
    /// cherchant les composants DLSS / Streamline / FSR3 / XeSS dans le dossier du jeu.
    /// </summary>
    DlssCompatibilityInfo GetCompatibility(string gameDir);

    /// <summary>
    /// Tente de trouver une jaquette pour un jeu sans image (Epic, Game Pass, dossier
    /// manuel) en interrogeant la recherche du magasin Steam par son nom. Renvoie une
    /// URL d'image ou null. Best-effort : aucune exception ne remonte.
    /// </summary>
    Task<string?> ResolveCoverUrlAsync(string gameName, CancellationToken ct);

    /// <summary>
    /// Installe le mod à partir du DLL embarqué dans CleanSlate : le DLL est copié
    /// À CÔTÉ DE L'EXÉCUTABLE du jeu (localisé automatiquement, y compris dans les
    /// sous-dossiers type Binaries\Win64 ou Content des jeux Game Pass), sous le nom
    /// de proxy le plus sûr pour ce jeu.
    /// </summary>
    Task<DlssInstallResult> InstallAsync(string gameDir, CancellationToken ct);

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
    ///
    /// On ne garde QUE les vrais jeux : « XboxGames » contient aussi des dossiers de
    /// service (sauvegardes « GameSave », fichiers temporaires…) qui ne sont pas des
    /// jeux. Un vrai jeu a un sous-dossier « Content » contenant un exécutable et/ou un
    /// manifeste Xbox (MicrosoftGame.config / appxmanifest.xml / AppxManifest.xml).
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
                if (!IsGamePassGameFolder(dir)) continue; // ignore « GameSave » & co.

                var content = Path.Combine(dir, "Content");
                yield return new InstalledGame(name, content, "Xbox Game Pass");
            }
        }
    }

    /// <summary>Noms de dossiers « XboxGames » qui ne sont jamais des jeux.</summary>
    private static readonly string[] NonGameXboxFolders =
    {
        "GameSave", "GameSaves", "Saves", "SaveGames",
    };

    /// <summary>
    /// Vrai si un sous-dossier de « XboxGames » est un vrai jeu : il possède un dossier
    /// « Content » contenant un manifeste Xbox ou au moins un exécutable.
    /// </summary>
    internal static bool IsGamePassGameFolder(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (NonGameXboxFolders.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        var content = Path.Combine(dir, "Content");
        if (!Directory.Exists(content)) return false;

        try
        {
            foreach (var manifest in new[] { "MicrosoftGame.config", "appxmanifest.xml", "AppxManifest.xml" })
                if (File.Exists(Path.Combine(content, manifest)))
                    return true;

            // À défaut de manifeste, la présence d'un exécutable suffit à confirmer un jeu.
            return Directory.EnumerateFiles(content, "*.exe").Any();
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    //  Localisation de l'exécutable du jeu
    // ------------------------------------------------------------------

    /// <summary>
    /// Sous-dossiers à ignorer pendant la recherche de l'exécutable : outils
    /// redistribuables, anticheats, données — jamais l'exécutable du jeu.
    /// </summary>
    private static readonly string[] SkippedDirNames =
    {
        "_CommonRedist", "CommonRedist", "Redist", "Redistributables", "DirectX",
        "DotNet", "DotNetCore", "VCRedist", "EasyAntiCheat", "BattlEye", "Support",
        "Installers", "__Installer", "Engine",
    };

    /// <summary>
    /// Exécutables « utilitaires » à ignorer : installateurs, crash handlers,
    /// anticheats… jamais le binaire principal du jeu.
    /// </summary>
    internal static bool IsHelperExecutable(string fileName)
    {
        var n = Path.GetFileNameWithoutExtension(fileName);
        string[] fragments =
        {
            "unins", "setup", "install", "redist", "vcredist", "vc_redist",
            "dxsetup", "dxwebsetup", "oalinst", "crash", "EasyAntiCheat",
            "BattlEye", "BEService", "report", "cleanup", "activation",
            "touchup", "QuickSFV", "UnityCrashHandler",
        };
        return fragments.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Localise le dossier contenant l'EXÉCUTABLE principal du jeu : le proxy DLL doit
    /// être posé à côté de l'exe pour être chargé. Beaucoup de jeux ont leur exe dans
    /// un sous-dossier (Binaries\Win64 pour Unreal Engine, bin\x64, ou le contenu d'un
    /// jeu Game Pass) : on cherche le plus gros .exe (le binaire principal est presque
    /// toujours le plus volumineux), en ignorant les utilitaires (installateurs,
    /// anticheats, crash handlers).
    /// </summary>
    internal static string? FindExecutableDir(string gameDir, int maxDepth = 3)
    {
        string? bestDir = null;
        long bestSize = -1;

        void Walk(string dir, int depth)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.exe"); }
            catch { return; }

            foreach (var exe in files)
            {
                if (IsHelperExecutable(exe)) continue;
                try
                {
                    var size = new FileInfo(exe).Length;
                    if (size > bestSize)
                    {
                        bestSize = size;
                        bestDir = dir;
                    }
                }
                catch { }
            }

            if (depth >= maxDepth) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { return; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (SkippedDirNames.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Walk(sub, depth + 1);
            }
        }

        Walk(gameDir, 0);
        return bestDir;
    }

    /// <summary>Dossier où poser le proxy DLL : celui de l'exe, sinon la racine du jeu.</summary>
    internal static string ResolveInstallDir(string gameDir) =>
        FindExecutableDir(gameDir) ?? gameDir;

    /// <summary>
    /// Teste si l'écriture est possible dans un dossier (création + suppression d'un
    /// fichier témoin). Les dossiers des jeux Game Pass sont souvent verrouillés.
    /// </summary>
    internal static bool CanWriteTo(string dir)
    {
        var probe = Path.Combine(dir, $".cleanslate-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Tente de déverrouiller un dossier de jeu Game Pass en accordant la modification
    /// au groupe Utilisateurs (icacls, SID indépendant de la langue). Ne fonctionne que
    /// si CleanSlate s'exécute en administrateur — sinon échec silencieux, l'appelant
    /// re-teste l'écriture derrière.
    /// </summary>
    private static async Task TryUnlockDirectoryAsync(string dir, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("icacls",
                $"\"{dir}\" /grant *S-1-5-32-545:(OI)(CI)M")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            if (process is not null)
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    // ------------------------------------------------------------------
    //  Détection de l'état du mod
    // ------------------------------------------------------------------

    public DlssEnablerStatus GetStatus(string gameDir)
    {
        var detected = new List<string>();
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return new DlssEnablerStatus(false, detected, null);

        // Le mod peut être à la racine du jeu OU à côté de l'exécutable (sous-dossier
        // Binaries\Win64, bin\x64, contenu Game Pass…) : on inspecte les deux.
        var exeDir = ResolveInstallDir(gameDir);
        var dirs = string.Equals(exeDir, gameDir, StringComparison.OrdinalIgnoreCase)
            ? new[] { gameDir }
            : new[] { gameDir, exeDir };

        foreach (var dir in dirs)
        {
            var prefix = RelativePrefix(gameDir, dir);

            foreach (var file in DistinctiveFiles)
            {
                if (File.Exists(Path.Combine(dir, file)))
                    detected.Add(prefix + file);
            }
            // Variante « plugin ASI » : le .asi est posé dans le sous-dossier plugins.
            if (File.Exists(Path.Combine(dir, "plugins", "dlss-enabler.asi")))
                detected.Add(prefix + @"plugins\dlss-enabler.asi");

            // Un DLL « chargeur » (version.dll, dxgi.dll…) ne compte comme preuve
            // d'installation que si ses métadonnées de version confirment qu'il
            // appartient à DLSS Enabler — jamais celles d'un autre mod (ReShade…).
            foreach (var loader in LoaderFiles)
            {
                var path = Path.Combine(dir, loader);
                if (File.Exists(path) && IsDlssEnablerBinary(path))
                    detected.Add(prefix + loader);
            }
        }

        bool installed = detected.Count > 0;
        return new DlssEnablerStatus(installed, detected, FindUninstaller(gameDir));
    }

    /// <summary>Préfixe relatif lisible (« bin\x64\ ») d'un sous-dossier du jeu, vide pour la racine.</summary>
    private static string RelativePrefix(string gameDir, string dir)
    {
        if (string.Equals(gameDir, dir, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        try
        {
            var rel = Path.GetRelativePath(gameDir, dir);
            return rel == "." ? string.Empty : rel + Path.DirectorySeparatorChar;
        }
        catch { return string.Empty; }
    }

    // ------------------------------------------------------------------
    //  Compatibilité du jeu avec DLSS Enabler
    // ------------------------------------------------------------------

    /// <summary>Composants DLSS / Streamline natifs : leur présence garantit la compatibilité.</summary>
    private static readonly string[] DlssMarkers =
    {
        "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll",
        "sl.interposer.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.common.dll",
    };

    /// <summary>Upscalers FSR / XeSS : DLSS Enabler peut s'appuyer dessus, sans garantie.</summary>
    private static readonly string[] OtherUpscalerMarkers =
    {
        "libxess.dll", "libxess_dx11.dll", "xess.dll",
        "amd_fidelityfx_dx12.dll", "amd_fidelityfx_vk.dll", "ffx_fsr2_api_x64.dll",
        "amdxcffx64.dll",
    };

    public DlssCompatibilityInfo GetCompatibility(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return new DlssCompatibilityInfo(DlssCompatibility.Unlikely, Array.Empty<string>(),
                "Dossier introuvable.");

        var found = FindMarkers(gameDir, DlssMarkers.Concat(OtherUpscalerMarkers).ToArray(), maxDepth: 4);

        var dlss  = found.Where(f => DlssMarkers.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList();
        var other = found.Where(f => OtherUpscalerMarkers.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList();

        var evidence = found.Select(Path.GetFileName).Where(n => n is not null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (dlss.Count > 0)
            return new DlssCompatibilityInfo(DlssCompatibility.Compatible, evidence,
                "✅ Ce jeu supporte DLSS nativement : DLSS Enabler pourra activer le DLSS et la " +
                "Frame Generation (y compris le Multi Frame Generation jusqu'à x6, DLSS 4.5).");

        if (other.Count > 0)
            return new DlssCompatibilityInfo(DlssCompatibility.Maybe, evidence,
                "🟡 Pas de DLSS natif, mais un upscaler (FSR/XeSS) est présent : DLSS Enabler PEUT " +
                "aider (Frame Generation via FSR3), sans garantie. À tester.");

        return new DlssCompatibilityInfo(DlssCompatibility.Unlikely, evidence,
            "⚠️ Aucun composant DLSS/FSR/XeSS détecté : ce jeu ne supporte probablement pas le DLSS. " +
            "DLSS Enabler n'ajoute pas le DLSS à un jeu qui n'en a pas — l'installer ici n'aura sans " +
            "doute AUCUN effet.");
    }

    /// <summary>Cherche des fichiers cibles (par nom) dans un dossier, en profondeur bornée.</summary>
    private static List<string> FindMarkers(string root, string[] names, int maxDepth)
    {
        var hits = new List<string>();
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        void Walk(string dir, int depth)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.dll"))
                    if (wanted.Contains(Path.GetFileName(file)))
                        hits.Add(file);
            }
            catch { return; }

            if (depth >= maxDepth) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { return; }
            foreach (var sub in subs)
                Walk(sub, depth + 1);
        }

        Walk(root, 0);
        return hits;
    }

    // ------------------------------------------------------------------
    //  Jaquette par nom (jeux hors Steam)
    // ------------------------------------------------------------------

    public async Task<string?> ResolveCoverUrlAsync(string gameName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-DlssEnabler");

            var url = "https://store.steampowered.com/api/storesearch/?cc=us&l=en&term=" +
                      Uri.EscapeDataString(gameName);
            var result = await http.GetFromJsonAsync<StoreSearchResult>(url, ct).ConfigureAwait(false);

            var appId = result?.Items?.FirstOrDefault(i => i.Id > 0)?.Id;
            return appId is > 0 ? SteamCoverUrl(appId.Value.ToString()) : null;
        }
        catch { return null; }
    }

    private sealed class StoreSearchResult
    {
        [JsonPropertyName("items")] public List<StoreSearchItem>? Items { get; set; }
    }

    private sealed class StoreSearchItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
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
    /// À CÔTÉ DE L'EXÉCUTABLE du jeu sous le nom de proxy choisi par
    /// <see cref="ChooseProxyFileName"/> (ou en variante plugin ASI si tous les noms
    /// de proxy sont déjà pris par un autre mod). Pour les jeux Game Pass au dossier
    /// verrouillé, tente d'abord un déverrouillage (efficace si CleanSlate est en
    /// administrateur), sinon renvoie <see cref="DlssInstallFailure.WriteDenied"/>.
    /// </summary>
    public async Task<DlssInstallResult> InstallAsync(string gameDir, CancellationToken ct)
    {
        if (!Directory.Exists(gameDir))
            return new DlssInstallResult(false, null, null, DlssInstallFailure.GameDirMissing);

        var targetDir = ResolveInstallDir(gameDir);

        if (!CanWriteTo(targetDir))
        {
            // Dossier verrouillé (Game Pass) : tentative de déverrouillage via icacls
            // (n'aboutit que si le processus est élevé), puis re-test.
            await TryUnlockDirectoryAsync(targetDir, ct).ConfigureAwait(false);
            if (!CanWriteTo(targetDir))
                return new DlssInstallResult(false, null, targetDir, DlssInstallFailure.WriteDenied);
        }

        byte[] dll;
        try { dll = ReadEmbeddedDll(); }
        catch { return new DlssInstallResult(false, null, targetDir, DlssInstallFailure.Unknown); }

        var proxyName = ChooseProxyFileName(targetDir);
        string installedFile;
        try
        {
            if (proxyName is not null)
            {
                await File.WriteAllBytesAsync(Path.Combine(targetDir, proxyName), dll, ct).ConfigureAwait(false);
                installedFile = proxyName;
            }
            else
            {
                // Tous les noms de proxy DLL sont déjà occupés par un autre mod : on pose
                // le mod sous forme de plugin ASI (nécessite un loader ASI, généralement
                // déjà présent dans les jeux fortement moddés qui occupent ces DLL).
                var pluginsDir = Path.Combine(targetDir, "plugins");
                Directory.CreateDirectory(pluginsDir);
                await File.WriteAllBytesAsync(Path.Combine(pluginsDir, "dlss-enabler.asi"), dll, ct).ConfigureAwait(false);
                installedFile = @"plugins\dlss-enabler.asi";
            }
        }
        catch
        {
            return new DlssInstallResult(false, null, targetDir, DlssInstallFailure.Unknown);
        }

        var ok = GetStatus(gameDir).Installed;
        return new DlssInstallResult(ok, ok ? installedFile : null, targetDir,
            ok ? DlssInstallFailure.None : DlssInstallFailure.Unknown);
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

        // 2. Repli : suppression manuelle des fichiers du mod — à la racine ET à côté
        //    de l'exécutable. Les DLL « chargeurs » (version.dll, dxgi.dll…) ne sont
        //    supprimées que si leurs métadonnées confirment qu'elles appartiennent à
        //    DLSS Enabler — jamais celles d'un autre mod (ReShade par exemple).
        var exeDir = ResolveInstallDir(gameDir);
        var dirs = string.Equals(exeDir, gameDir, StringComparison.OrdinalIgnoreCase)
            ? new[] { gameDir }
            : new[] { gameDir, exeDir };

        bool removedAny = false;
        foreach (var dir in dirs)
        {
            foreach (var file in DistinctiveFiles)
                removedAny |= TryDelete(Path.Combine(dir, file));
            removedAny |= TryDelete(Path.Combine(dir, "plugins", "dlss-enabler.asi"));

            foreach (var loader in LoaderFiles)
            {
                var path = Path.Combine(dir, loader);
                if (File.Exists(path) && IsDlssEnablerBinary(path))
                    removedAny |= TryDelete(path);
            }
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
