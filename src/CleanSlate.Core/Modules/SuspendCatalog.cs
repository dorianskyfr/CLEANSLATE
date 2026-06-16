namespace CleanSlate.Core.Modules;

/// <summary>
/// Niveau (profil) à partir duquel une application est suspendue en Mode Jeu :
///  - <see cref="Leger"/>   : seulement les gros consommateurs d'arrière-plan évidents
///    (synchro cloud, mises à jour) — aucun impact sur l'usage courant.
///  - <see cref="Equilibre"/> : + messageries, lecteurs multimédia, lanceurs de jeux
///    inactifs, assistants — le profil recommandé pour la plupart des joueurs.
///  - <see cref="Agressif"/> : + navigateurs, logiciels RGB/périphériques, etc. — gain
///    maximal, mais coupe des apps que certains gardent ouvertes en jouant.
/// Une application d'un niveau donné est incluse dans ce niveau ET tous les niveaux
/// supérieurs (Léger ⊂ Équilibré ⊂ Agressif).
/// </summary>
public enum SuspendTier { Leger, Equilibre, Agressif }

/// <summary>
/// Une application connue, candidate à la suspension en Mode Jeu. <see cref="ProcessName"/>
/// est le nom du processus sans « .exe ». <see cref="MinTier"/> indique le profil minimal
/// qui l'inclut. <see cref="Note"/> explique pourquoi (ou pourquoi prudence).
/// </summary>
public sealed record SuspendableApp(
    string ProcessName,
    string DisplayName,
    string Category,
    SuspendTier MinTier,
    string Note);

/// <summary>
/// Catalogue d'applications d'arrière-plan courantes, classées par catégorie et par
/// profil de suspension. Sert à la fois la liste « à cocher » de l'interface et les
/// profils prêts à l'emploi (Léger / Équilibré / Agressif).
///
/// SÛRETÉ : aucune entrée n'est un processus système Windows. Discord est volontairement
/// ABSENT (le vocal reste actif en jeu). On SUSPEND (pause), on ne tue pas : tout est
/// repris à la désactivation du Mode Jeu.
/// </summary>
public static class SuspendCatalog
{
    public static readonly IReadOnlyList<SuspendableApp> Apps = new[]
    {
        // ─── Stockage cloud (synchro permanente, lourde en disque/réseau) ──────────
        new SuspendableApp("OneDrive",     "OneDrive",          "Stockage cloud", SuspendTier.Leger, "Synchro Microsoft — sans intérêt pendant une partie."),
        new SuspendableApp("Dropbox",      "Dropbox",           "Stockage cloud", SuspendTier.Leger, "Synchro de fichiers en arrière-plan."),
        new SuspendableApp("GoogleDriveFS","Google Drive",      "Stockage cloud", SuspendTier.Leger, "Synchro Google Drive."),
        new SuspendableApp("Creative Cloud","Adobe Creative Cloud","Stockage cloud", SuspendTier.Equilibre, "Synchro et mises à jour Adobe."),

        // ─── Mises à jour / installeurs (téléchargent en tâche de fond) ────────────
        new SuspendableApp("EpicGamesLauncher","Epic Games Launcher","Lanceurs de jeux", SuspendTier.Leger, "Inutile une fois le jeu lancé."),
        new SuspendableApp("EpicWebHelper", "Epic Web Helper",   "Lanceurs de jeux", SuspendTier.Leger, "Processus auxiliaire d'Epic."),
        new SuspendableApp("GalaxyClient",  "GOG Galaxy",        "Lanceurs de jeux", SuspendTier.Equilibre, "Lanceur GOG inactif pendant le jeu."),
        new SuspendableApp("UbisoftConnect","Ubisoft Connect",   "Lanceurs de jeux", SuspendTier.Equilibre, "À garder actif seulement pour les jeux Ubisoft."),
        new SuspendableApp("upc",           "Ubisoft Connect (upc)","Lanceurs de jeux", SuspendTier.Equilibre, "Composant Ubisoft Connect."),
        new SuspendableApp("EADesktop",     "EA App",            "Lanceurs de jeux", SuspendTier.Equilibre, "À garder actif seulement pour les jeux EA."),
        new SuspendableApp("Origin",        "Origin (EA)",       "Lanceurs de jeux", SuspendTier.Equilibre, "Ancien lanceur EA."),
        new SuspendableApp("Battle.net",    "Battle.net",        "Lanceurs de jeux", SuspendTier.Equilibre, "À garder actif seulement pour les jeux Blizzard."),

        // ─── Communication (Discord exclu volontairement) ──────────────────────────
        new SuspendableApp("Spotify",       "Spotify",           "Communication & médias", SuspendTier.Equilibre, "Coupez-le si vous écoutez la musique du jeu."),
        new SuspendableApp("Slack",         "Slack",             "Communication & médias", SuspendTier.Equilibre, "Messagerie pro."),
        new SuspendableApp("Teams",         "Microsoft Teams",   "Communication & médias", SuspendTier.Equilibre, "Visio/chat pro."),
        new SuspendableApp("ms-teams",      "Microsoft Teams (nouv.)","Communication & médias", SuspendTier.Equilibre, "Nouvelle app Teams."),
        new SuspendableApp("Skype",         "Skype",             "Communication & médias", SuspendTier.Equilibre, "Messagerie."),
        new SuspendableApp("Telegram",      "Telegram",          "Communication & médias", SuspendTier.Equilibre, "Messagerie."),
        new SuspendableApp("WhatsApp",      "WhatsApp",          "Communication & médias", SuspendTier.Equilibre, "Messagerie."),
        new SuspendableApp("Zoom",          "Zoom",              "Communication & médias", SuspendTier.Equilibre, "Visioconférence."),

        // ─── Accès distant (gourmands, inutiles en jeu) ────────────────────────────
        new SuspendableApp("AnyDesk",       "AnyDesk",           "Accès distant", SuspendTier.Leger, "Bureau à distance."),
        new SuspendableApp("TeamViewer",    "TeamViewer",        "Accès distant", SuspendTier.Leger, "Bureau à distance."),

        // ─── Assistants & arrière-plan Windows ─────────────────────────────────────
        new SuspendableApp("Cortana",       "Cortana",           "Assistants & système", SuspendTier.Equilibre, "Assistant vocal Windows."),
        new SuspendableApp("SearchApp",     "Recherche Windows", "Assistants & système", SuspendTier.Agressif, "Interface de recherche du menu Démarrer."),
        new SuspendableApp("yourphone",     "Lien avec le téléphone","Assistants & système", SuspendTier.Equilibre, "Synchro téléphone Microsoft."),
        new SuspendableApp("WinStore.App",  "Microsoft Store",   "Assistants & système", SuspendTier.Equilibre, "Boutique Microsoft."),

        // ─── Suite Adobe (helpers gourmands) ───────────────────────────────────────
        new SuspendableApp("CCLibrary",     "Adobe CC Library",  "Création (Adobe)", SuspendTier.Equilibre, "Composant Adobe en arrière-plan."),
        new SuspendableApp("AdobeIPCBroker","Adobe IPC Broker",  "Création (Adobe)", SuspendTier.Equilibre, "Composant Adobe en arrière-plan."),

        // ─── Logiciels RGB / périphériques (souvent lourds) ────────────────────────
        new SuspendableApp("iCUE",          "Corsair iCUE",      "RGB & périphériques", SuspendTier.Agressif, "⚠️ Vos effets RGB se figent jusqu'à la reprise."),
        new SuspendableApp("LogiOptionsPlus","Logitech Options+", "RGB & périphériques", SuspendTier.Agressif, "Réglages souris/clavier Logitech."),
        new SuspendableApp("RzSynapse",     "Razer Synapse",     "RGB & périphériques", SuspendTier.Agressif, "⚠️ Macros/RGB Razer en pause."),

        // ─── Navigateurs (à n'inclure que dans le profil agressif) ─────────────────
        new SuspendableApp("chrome",        "Google Chrome",     "Navigateurs", SuspendTier.Agressif, "⚠️ Onglets gelés (musique, guides…) jusqu'à la reprise."),
        new SuspendableApp("firefox",       "Mozilla Firefox",   "Navigateurs", SuspendTier.Agressif, "⚠️ Onglets gelés jusqu'à la reprise."),
        new SuspendableApp("msedge",        "Microsoft Edge",    "Navigateurs", SuspendTier.Agressif, "⚠️ Onglets gelés jusqu'à la reprise."),
        new SuspendableApp("opera",         "Opera",             "Navigateurs", SuspendTier.Agressif, "⚠️ Onglets gelés jusqu'à la reprise."),
    };

    /// <summary>Noms de processus inclus dans un profil donné (ce niveau et inférieurs).</summary>
    public static IReadOnlyList<string> ProcessesFor(SuspendTier tier) =>
        Apps.Where(a => a.MinTier <= tier)
            .Select(a => a.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Catégories dans l'ordre d'affichage (ordre de première apparition).</summary>
    public static IReadOnlyList<string> Categories =>
        Apps.Select(a => a.Category).Distinct().ToArray();
}
