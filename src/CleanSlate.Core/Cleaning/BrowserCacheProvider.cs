using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Nettoyage du cache des principaux navigateurs (Chrome, Edge, Brave, Opera, Firefox).
/// Énumère dynamiquement tous les profils pour couvrir les configs multi-profils.
/// Ne touche jamais aux mots de passe, favoris ou historique — cache uniquement.
/// </summary>
public sealed class BrowserCacheProvider : FileCleaningProviderBase
{
    public BrowserCacheProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "browser-cache";
    public override string DisplayName => "Cache des navigateurs";
    public override CleaningCategory Category => CleaningCategory.CacheNavigateurs;
    public override CleaningSeverity Severity => CleaningSeverity.Information;

    public override string Description =>
        "Vide le cache de Chrome, Edge, Brave, Opera et Firefox. Fermez vos navigateurs avant le " +
        "nettoyage, sinon des fichiers seront verrouillés. N'affecte ni vos mots de " +
        "passe, ni vos favoris, ni votre historique — uniquement les fichiers de cache.";

    protected override IReadOnlyList<CleaningTarget> Targets => BuildTargets();

    private static IReadOnlyList<CleaningTarget> BuildTargets()
    {
        var targets = new List<CleaningTarget>();

        // ── Navigateurs Chromium (Chrome, Edge, Brave, Vivaldi, Canary/Beta) ──
        // Chacun utilise la structure "User Data\Default" et "User Data\Profile N".
        string[] chromiumUserDataDirs =
        {
            @"%LOCALAPPDATA%\Google\Chrome\User Data",
            @"%LOCALAPPDATA%\Google\Chrome Beta\User Data",
            @"%LOCALAPPDATA%\Google\Chrome SxS\User Data",
            @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
            @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",
            @"%LOCALAPPDATA%\Vivaldi\User Data",
        };

        foreach (var template in chromiumUserDataDirs)
            AddChromiumProfiles(targets, template);

        // ── Opera / Opera GX ──
        // Opera n'a PAS de sous-dossier "Default" : le dossier de profil EST la racine,
        // et le cache réel vit sous %LOCALAPPDATA% (le profil sous %APPDATA%). On scanne
        // donc les sous-dossiers de cache DIRECTEMENT sous chacun de ces emplacements
        // (l'ancien code cherchait "Opera Stable\Default\Cache", qui n'existe jamais —
        // le cache Opera/Opera GX n'était donc jamais nettoyé).
        foreach (var operaRoot in new[]
                 {
                     @"%APPDATA%\Opera Software\Opera Stable",
                     @"%LOCALAPPDATA%\Opera Software\Opera Stable",
                     @"%APPDATA%\Opera Software\Opera GX Stable",
                     @"%LOCALAPPDATA%\Opera Software\Opera GX Stable",
                 })
            AddDirectProfileCaches(targets, ExpandPath(operaRoot));

        // ── Firefox — profils dans Profiles\ ──
        var ffProfiles = ExpandPath(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
        if (Directory.Exists(ffProfiles))
        {
            foreach (var profileDir in SafeListDirectories(ffProfiles))
            {
                foreach (var sub in new[] { "cache2", "startupCache" })
                {
                    var p = Path.Combine(profileDir, sub);
                    if (Directory.Exists(p))
                        targets.Add(new CleaningTarget(p, CleaningCategory.CacheNavigateurs));
                }
            }
        }

        return targets;
    }

    private static void AddChromiumProfiles(List<CleaningTarget> targets, string userDataTemplate)
    {
        var userDataDir = ExpandPath(userDataTemplate);
        if (!Directory.Exists(userDataDir)) return;

        // Default + tous les "Profile N"
        var profiles = new List<string> { Path.Combine(userDataDir, "Default") };
        profiles.AddRange(
            SafeListDirectories(userDataDir)
                .Where(d => Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)));

        foreach (var profile in profiles.Where(Directory.Exists))
            AddDirectProfileCaches(targets, profile);
    }

    /// <summary>Ajoute les sous-dossiers de cache Chromium présents DIRECTEMENT sous <paramref name="profileDir"/>.</summary>
    private static void AddDirectProfileCaches(List<CleaningTarget> targets, string profileDir)
    {
        if (!Directory.Exists(profileDir)) return;

        // Cache, Code Cache (JS/WASM), GPU Cache
        foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
        {
            var p = Path.Combine(profileDir, sub);
            if (Directory.Exists(p))
                targets.Add(new CleaningTarget(p, CleaningCategory.CacheNavigateurs));
        }
    }

    private static IEnumerable<string> SafeListDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}

