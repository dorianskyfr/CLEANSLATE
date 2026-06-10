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
        IGpuDriverChecker driverChecker,
        IDialogService dialogs)
    {
        _gameMode = gameMode;
        _dialogs = dialogs;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        Overclocking = new OverclockingViewModel(overclockingAdvisor, overclocker, driverChecker, dialogs);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    /// <summary>Sous-catégorie « Overclocking » (détection GPU + profil recommandé).</summary>
    public OverclockingViewModel Overclocking { get; }

    public bool IsActive => _gameMode.IsActive;
    public string ToggleLabel => IsActive ? "Désactiver le Mode Jeu" : "Activer le Mode Jeu";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Processus suspendus lors de l'activation.</summary>
    public string SuspendedAppsList =>
        string.Join("  •  ", GameModeOptions.Default.ProcessNamesToSuspend);

    /// <summary>Services arrêtés lors de l'activation.</summary>
    public string StoppedServicesList =>
        string.Join("  •  ", GameModeOptions.Default.ServiceNamesToStop);

    public string HonestNotice =>
        "Le Mode Jeu suspend (sans les fermer) les applications d'arrière-plan non " +
        "essentielles et arrête temporairement quelques services Windows, puis " +
        "restaure l'état initial à la désactivation. Discord reste actif. " +
        "Les gains varient selon la machine — en cas de fermeture brutale, " +
        "l'état est restauré au prochain démarrage.";

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
