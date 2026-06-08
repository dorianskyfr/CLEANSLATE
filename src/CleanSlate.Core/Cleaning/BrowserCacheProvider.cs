using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Nettoyage du cache des principaux navigateurs (Chrome, Edge, Firefox).
///
/// LIMITE HONNÊTE : si le navigateur est ouvert, beaucoup de fichiers de cache
/// sont verrouillés et ne pourront pas être supprimés. L'UI recommande de fermer
/// le navigateur avant le nettoyage. Vider le cache entraîne un re-téléchargement
/// des ressources (léger ralentissement temporaire à la prochaine navigation).
///
/// SÉCURITÉ : on vise UNIQUEMENT les dossiers de cache — jamais l'historique, les
/// mots de passe, les cookies ou les profils, qui sont des données utilisateur.
/// Pour Firefox, le nom du profil est aléatoire (ex. "xxxx.default-release") ;
/// on résout donc dynamiquement le sous-dossier "cache2" de CHAQUE profil, sans
/// jamais cibler la racine "Profiles" (qui contient les données personnelles).
/// </summary>
public sealed class BrowserCacheProvider : FileCleaningProviderBase
{
    public BrowserCacheProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "browser-cache";
    public override string DisplayName => "Cache des navigateurs";
    public override CleaningCategory Category => CleaningCategory.CacheNavigateurs;
    public override CleaningSeverity Severity => CleaningSeverity.Information;

    public override string Description =>
        "Vide le cache de Chrome, Edge et Firefox. Fermez vos navigateurs avant le " +
        "nettoyage, sinon des fichiers seront verrouillés. N'affecte ni vos mots de " +
        "passe, ni vos favoris, ni votre historique — uniquement les fichiers de cache.";

    // Targets calculée à chaque accès : permet de découvrir dynamiquement les
    // profils Firefox tout en gardant le modèle déclaratif pour Chrome/Edge.
    protected override IReadOnlyList<CleaningTarget> Targets => BuildTargets();

    private static IReadOnlyList<CleaningTarget> BuildTargets()
    {
        var targets = new List<CleaningTarget>
        {
            // Google Chrome — dossiers de cache du profil par défaut uniquement.
            new(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache",
                CleaningCategory.CacheNavigateurs),
            new(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache",
                CleaningCategory.CacheNavigateurs),

            // Microsoft Edge — idem.
            new(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache",
                CleaningCategory.CacheNavigateurs),
            new(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache",
                CleaningCategory.CacheNavigateurs),
        };

        // Firefox : pour chaque profil, ne cibler que <profil>\cache2.
        var firefoxProfiles = ExpandPath(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (var profileDir in SafeListDirectories(firefoxProfiles))
            {
                var cache2 = Path.Combine(profileDir, "cache2");
                if (Directory.Exists(cache2))
                    targets.Add(new CleaningTarget(cache2, CleaningCategory.CacheNavigateurs));
            }
        }

        return targets;
    }

    private static IEnumerable<string> SafeListDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
