namespace CleanSlate.Core.Abstractions;

/// <summary>
/// Journalisation des actions sensibles (nettoyages, modifications de registre,
/// suspensions de processus…). Indispensable pour la traçabilité et le SAV
/// utilisateur (« qu'est-ce qui a été supprimé ? »).
/// </summary>
public interface IActionLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);

    /// <summary>Enregistre une action de nettoyage terminée (résumé chiffré).</summary>
    void LogCleaning(string providerId, int deleted, int failed, long freedBytes);
}
