using System.Text;
using CleanSlate.Core.Diagnostics;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Génère un rapport système texte, lisible et copiable (pour du dépannage ou du support).
/// Fonction pure : prend un instantané système + un score de santé et renvoie du texte.
/// </summary>
public static class SystemReport
{
    public static string Build(SystemOverview info, HealthReport health, DateTime generatedAtLocal)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== Rapport système CleanSlate =====");
        sb.AppendLine($"Généré le : {generatedAtLocal:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Santé système : {health.Score}/100 ({health.Rating})");
        sb.AppendLine();
        sb.AppendLine($"Windows          : {info.OsName}");
        sb.AppendLine($"Processeur       : {info.CpuName} ({info.LogicalCores} threads)");
        sb.AppendLine($"Mémoire installée: {FileActionLogger.FormatBytes((long)info.TotalRamBytes)} " +
                      $"(charge actuelle {info.MemoryLoadPercent} %)");
        sb.AppendLine($"Allumé depuis    : {FormatUptime(info.Uptime)}");
        sb.AppendLine();
        sb.AppendLine("Disques :");
        foreach (var d in info.Drives)
        {
            var label = string.IsNullOrEmpty(d.Label) ? "" : $" ({d.Label})";
            sb.AppendLine($"  - {d.Name}{label} : {FileActionLogger.FormatBytes(d.FreeBytes)} libres " +
                          $"sur {FileActionLogger.FormatBytes(d.TotalBytes)} — {d.UsedPercent:0} % utilisé");
        }

        if (health.Tips.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Conseils :");
            foreach (var tip in health.Tips)
                sb.AppendLine($"  - {tip}");
        }

        sb.AppendLine();
        sb.AppendLine("Généré par CleanSlate — https://github.com/dorianskyfr/CLEANSLATE");
        return sb.ToString();
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1
            ? $"{(int)t.TotalDays} j {t.Hours} h {t.Minutes} min"
            : $"{t.Hours} h {t.Minutes} min";
}
