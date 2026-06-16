namespace CleanSlate.Core.Modules;

/// <summary>
/// Un jeu open-source téléchargeable légalement. <paramref name="DownloadUrl"/> pointe
/// vers le binaire officiel du projet (HTTPS, dépôt officiel / SourceForge « latest »),
/// <paramref name="OfficialUrl"/> vers le site du projet. Aucune « source » externe, aucun
/// magnet : uniquement des jeux libres distribués par leurs auteurs.
/// </summary>
public sealed record OpenSourceGame(
    string Name,
    string Summary,
    string DownloadUrl,
    string OfficialUrl);

/// <summary>
/// Catalogue CURÉ de jeux open-source (libres de droits), téléchargeables depuis leurs
/// dépôts officiels. Liste fixe et vérifiable — contrairement à un système de « sources »
/// JSON arbitraires, elle ne peut pas servir à distribuer des jeux piratés.
///
/// Les URLs « .../files/latest/download » de SourceForge sont des redirections stables
/// vers la dernière version Windows publiée par le projet.
/// </summary>
public static class OpenSourceGameCatalog
{
    public static IReadOnlyList<OpenSourceGame> Games { get; } = new[]
    {
        new OpenSourceGame(
            "SuperTuxKart",
            "Jeu de course de kart arcade (style Mario Kart), entièrement libre.",
            "https://sourceforge.net/projects/supertuxkart/files/latest/download",
            "https://supertuxkart.net/"),

        new OpenSourceGame(
            "The Battle for Wesnoth",
            "Jeu de stratégie au tour par tour en fantasy, campagnes solo et multijoueur.",
            "https://sourceforge.net/projects/wesnoth/files/latest/download",
            "https://www.wesnoth.org/"),

        new OpenSourceGame(
            "Warzone 2100",
            "Jeu de stratégie en temps réel post-apocalyptique (ex-commercial, désormais libre).",
            "https://sourceforge.net/projects/warzone2100/files/latest/download",
            "https://wz2100.net/"),

        new OpenSourceGame(
            "Hedgewars",
            "Jeu d'artillerie au tour par tour façon Worms, avec des hérissons.",
            "https://sourceforge.net/projects/hedgewars/files/latest/download",
            "https://www.hedgewars.org/"),

        new OpenSourceGame(
            "FlightGear",
            "Simulateur de vol open-source multiplateforme, très complet.",
            "https://sourceforge.net/projects/flightgear/files/latest/download",
            "https://www.flightgear.org/"),

        new OpenSourceGame(
            "Frozen Bubble",
            "Le célèbre puzzle de tir de bulles, en version libre.",
            "https://sourceforge.net/projects/frozen-bubble/files/latest/download",
            "http://www.frozen-bubble.org/"),
    };
}
