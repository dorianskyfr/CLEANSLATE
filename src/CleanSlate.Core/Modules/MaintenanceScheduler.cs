namespace CleanSlate.Core.Modules;

/// <summary>
/// Décision pure du planificateur d'entretien : faut-il lancer un entretien
/// automatique maintenant ? Sans état ni effet de bord — testable simplement.
/// </summary>
public static class MaintenanceScheduler
{
    /// <summary>
    /// Vrai si l'entretien automatique est activé et que l'intervalle depuis le
    /// dernier passage est écoulé. Un <paramref name="intervalHours"/> ≤ 0 est
    /// ramené à 24 h par sécurité.
    /// </summary>
    public static bool ShouldRun(bool enabled, int intervalHours, DateTime lastRunUtc, DateTime nowUtc)
    {
        if (!enabled) return false;
        if (intervalHours <= 0) intervalHours = 24;
        return nowUtc - lastRunUtc >= TimeSpan.FromHours(intervalHours);
    }
}
