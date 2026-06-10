using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Module 4 — Mode Jeu. Bascule l'activation/désactivation, avec restauration
/// systématique. Affiche honnêtement la portée de l'action.
/// </summary>
public sealed class GameModeViewModel : ObservableObject
{
    private readonly IGameMode _gameMode;
    private readonly IDialogService _dialogs;
    private string _status = "Mode Jeu inactif.";

    public GameModeViewModel(
        IGameMode gameMode,
        IOverclockingAdvisor overclockingAdvisor,
        IGpuOverclocker overclocker,
        IDialogService dialogs)
    {
        _gameMode = gameMode;
        _dialogs = dialogs;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        Overclocking = new OverclockingViewModel(overclockingAdvisor, overclocker, dialogs);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    /// <summary>Sous-catégorie « Overclocking » (détection GPU + profil recommandé).</summary>
    public OverclockingViewModel Overclocking { get; }

    public bool IsActive => _gameMode.IsActive;
    public string ToggleLabel => IsActive ? "Désactiver le Mode Jeu" : "Activer le Mode Jeu";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Liste (affichée) des applications suspendues par défaut.</summary>
    public string SuspendedAppsList =>
        string.Join(", ", GameModeOptions.Default.ProcessNamesToSuspend);

    public string HonestNotice =>
        "Le Mode Jeu suspend (sans les fermer) quelques applications d'arrière-plan " +
        "non essentielles, puis les reprend automatiquement à la désactivation. Les " +
        "gains de performance sont très variables selon votre machine. Aucun processus " +
        "système n'est touché. En cas de fermeture inattendue de l'app, l'état est " +
        "restauré au prochain démarrage.";

    private async Task ToggleAsync()
    {
        try
        {
            if (_gameMode.IsActive)
            {
                await _gameMode.RestoreAsync(CancellationToken.None);
                Status = "Mode Jeu désactivé : applications reprises.";
            }
            else
            {
                var snap = await _gameMode.ActivateAsync(GameModeOptions.Default, CancellationToken.None);
                Status = $"Mode Jeu actif : {snap.SuspendedProcessIds.Count} application(s) suspendue(s).";
            }
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Mode Jeu", ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ToggleLabel));
        }
    }
}
