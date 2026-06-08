namespace CleanSlate.Core.Modules;

/// <summary>Entrée de démarrage automatique (clé Run, dossier Startup, tâche planifiée).</summary>
public sealed record StartupEntry(
    string Name,
    string Command,
    StartupLocation Location,
    bool IsEnabled);

public enum StartupLocation
{
    RegistryRunCurrentUser,   // HKCU\...\Run
    RegistryRunLocalMachine,  // HKLM\...\Run
    StartupFolder,            // dossier Démarrage
    ScheduledTask,            // tâche planifiée au logon
}

/// <summary>
/// Module 5a — Gestion des programmes au démarrage.
/// Optimisation la plus RENTABLE et la plus SÛRE : on DÉSACTIVE (réversible)
/// plutôt que de supprimer. Gain réel sur le temps de démarrage.
/// </summary>
public interface IStartupManager
{
    IReadOnlyList<StartupEntry> ListStartupEntries();
    void SetEnabled(StartupEntry entry, bool enabled);
}

/// <summary>
/// Module 5b — Nettoyage du registre.
///
/// ⚠️ À DIRE FRANCHEMENT (voir docs/LIMITES-TECHNIQUES.md) : le gain de
/// performance est quasi NUL, le risque est réel. C'est pourquoi le contrat
/// IMPOSE une sauvegarde : aucune suppression sans <see cref="IBackup"/> réussie.
///
/// Le ciblage doit rester conservateur (entrées manifestement orphelines).
/// </summary>
public interface IRegistryCleaner
{
    /// <summary>Analyse SANS rien modifier : liste des entrées orphelines candidates.</summary>
    Task<IReadOnlyList<RegistryIssue>> ScanAsync(CancellationToken ct);

    /// <summary>
    /// Corrige les entrées fournies APRÈS qu'une sauvegarde (.reg) a été créée.
    /// L'implémentation DOIT refuser d'agir si la sauvegarde n'a pas réussi.
    /// </summary>
    Task<int> FixAsync(IReadOnlyCollection<RegistryIssue> issues, string backupFilePath, CancellationToken ct);
}

/// <summary>Problème de registre détecté (purement informatif tant que non corrigé).</summary>
public sealed record RegistryIssue(string KeyPath, string ValueName, string Reason);
