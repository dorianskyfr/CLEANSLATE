namespace CleanSlate.Core.Modules;

/// <summary>
/// Options d'activation du Mode Jeu. Listes BLANCHES conservatrices : on ne touche
/// qu'à ce qui est explicitement déclaré sûr.
/// </summary>
public sealed class GameModeOptions
{
    /// <summary>Noms de processus (sans .exe) à suspendre, ex. "OneDrive", "Spotify".</summary>
    public IReadOnlyCollection<string> ProcessNamesToSuspend { get; init; } = Array.Empty<string>();

    /// <summary>Noms de services Windows non essentiels à arrêter temporairement.</summary>
    public IReadOnlyCollection<string> ServiceNamesToStop { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Liste blanche par défaut : applications d'arrière-plan courantes, non
    /// critiques, sûres à suspendre pendant une session de jeu. JAMAIS de
    /// processus système.
    /// </summary>
    public static GameModeOptions Default => new()
    {
        ProcessNamesToSuspend = new[]
        {
            "OneDrive", "Dropbox", "GoogleDriveFS",
            "Spotify", "Slack", "Teams",
            // Discord conservé actif : l'utilisateur peut être en communication vocale
        },
        ServiceNamesToStop = Array.Empty<string>(), // vide par défaut : prudence
    };
}

/// <summary>État capturé avant l'activation du Mode Jeu, requis pour restaurer.</summary>
public sealed class GameModeSnapshot
{
    public List<int> SuspendedProcessIds { get; set; } = new();
    public List<string> StoppedServices { get; set; } = new();
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Module 4 — Mode Jeu.
///
/// PRINCIPE DE SÛRETÉ : on capture l'état AVANT toute action et on le restaure
/// systématiquement à la sortie. Le snapshot est PERSISTÉ sur disque : si
/// l'application est fermée brutalement, <see cref="TryRecoverAsync"/> permet de
/// restaurer l'état au démarrage suivant (les processus suspendus sont repris).
///
/// On SUSPEND les processus (réversible) plutôt que de les tuer, et on se limite
/// à une liste blanche conservatrice. Voir docs/LIMITES-TECHNIQUES.md : les gains
/// FPS sont très variables selon la machine.
/// </summary>
public interface IGameMode
{
    bool IsActive { get; }

    /// <summary>Active le Mode Jeu : capture l'état, suspend les processus, arrête les services déclarés.</summary>
    Task<GameModeSnapshot> ActivateAsync(GameModeOptions options, CancellationToken ct);

    /// <summary>Désactive le Mode Jeu et RESTAURE l'état capturé.</summary>
    Task RestoreAsync(CancellationToken ct);

    /// <summary>
    /// Au démarrage de l'app : si un snapshot persistant subsiste (fermeture
    /// brutale lors d'une session précédente), restaure l'état. Retourne true si
    /// une récupération a eu lieu.
    /// </summary>
    Task<bool> TryRecoverAsync(CancellationToken ct);
}
