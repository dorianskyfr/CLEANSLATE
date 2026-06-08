namespace CleanSlate.Core.Modules;

/// <summary>État capturé avant l'activation du Mode Jeu, requis pour restaurer.</summary>
public sealed class GameModeSnapshot
{
    public List<int> SuspendedProcessIds { get; } = new();
    public List<string> StoppedServices { get; } = new();
    public bool FocusAssistWasEnabled { get; set; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Module 4 — Mode Jeu.
///
/// PRINCIPE DE SÛRETÉ : on capture l'état initial AVANT toute action, et on le
/// restaure systématiquement à la sortie (y compris au prochain démarrage si
/// l'application est fermée brutalement — d'où la persistance du snapshot).
///
/// On SUSPEND les processus (NtSuspendProcess) plutôt que de les TUER, et on se
/// limite à une LISTE BLANCHE conservatrice (jamais de processus système
/// critiques). Voir docs/LIMITES-TECHNIQUES.md : les gains FPS sont très variables.
/// </summary>
public interface IGameMode
{
    bool IsActive { get; }

    /// <summary>
    /// Active le Mode Jeu : capture l'état, suspend les processus de la liste sûre,
    /// met en pause notifications/services non essentiels. Retourne le snapshot.
    /// </summary>
    Task<GameModeSnapshot> ActivateAsync(IReadOnlyCollection<string> safeProcessNames, CancellationToken ct);

    /// <summary>Désactive le Mode Jeu et RESTAURE l'état capturé.</summary>
    Task RestoreAsync(GameModeSnapshot snapshot, CancellationToken ct);
}

// Implémentation à venir : NtSuspendProcess/NtResumeProcess (ntdll), ServiceController
// pour les services, et l'API Focus Assist. Toute la difficulté est dans la
// FIABILITÉ de la restauration — d'où l'interface centrée sur le snapshot.
