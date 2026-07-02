namespace CleanSlate.Core.Modules;

/// <summary>Signaux d'entrée pour le calcul du score de santé (tous mesurables honnêtement).</summary>
public sealed record HealthInputs(
    double UptimeDays,
    double MinFreeDiskPercent,
    double RamLoadPercent);

/// <summary>Résultat du score de santé : note /100, appréciation et conseils actionnables.</summary>
public sealed record HealthReport(int Score, string Rating, IReadOnlyList<string> Tips);

/// <summary>
/// Score de santé système — volontairement SIMPLE et HONNÊTE. Ce n'est pas une note
/// magique : c'est l'agrégation transparente de quelques signaux réels (espace disque,
/// charge mémoire, temps depuis le dernier redémarrage), chacun documenté par un conseil.
/// Fonction pure, sans effet de bord, entièrement testable.
/// </summary>
public static class HealthScore
{
    public static HealthReport Evaluate(HealthInputs i)
    {
        int score = 100;
        var tips = new List<string>();

        // Espace disque (le plus critique).
        if (i.MinFreeDiskPercent < 10)
        {
            score -= 25;
            tips.Add("💽 Un disque est presque plein (< 10 % libre) : Windows ralentit fortement " +
                     "en dessous. Videz le Nettoyage, l'analyseur d'espace ou les doublons.");
        }
        else if (i.MinFreeDiskPercent < 20)
        {
            score -= 10;
            tips.Add("💽 Espace disque faible (< 20 % libre) : pensez à faire un peu de place.");
        }

        // Charge mémoire.
        if (i.RamLoadPercent > 90)
        {
            score -= 15;
            tips.Add("📊 Mémoire très sollicitée (> 90 %) : fermez des applications ou lancez " +
                     "l'optimisation de la RAM.");
        }
        else if (i.RamLoadPercent > 80)
        {
            score -= 5;
            tips.Add("📊 Mémoire assez chargée (> 80 %).");
        }

        // Temps depuis le dernier redémarrage.
        if (i.UptimeDays >= 14)
        {
            score -= 15;
            tips.Add($"🔄 PC allumé depuis {(int)i.UptimeDays} jours : un redémarrage purge la mémoire " +
                     "et applique les mises à jour en attente.");
        }
        else if (i.UptimeDays >= 7)
        {
            score -= 8;
            tips.Add($"🔄 PC allumé depuis {(int)i.UptimeDays} jours : un redémarrage complet ferait du bien.");
        }

        if (score < 0) score = 0;
        if (score > 100) score = 100;

        return new HealthReport(score, RatingFor(score), tips);
    }

    internal static string RatingFor(int score) => score switch
    {
        >= 85 => "Excellent",
        >= 65 => "Bon",
        >= 40 => "Moyen",
        _     => "À surveiller",
    };
}
