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
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private string _status = "Mode Jeu inactif.";
    private string _customProcessesText;

    public GameModeViewModel(
        IGameMode gameMode,
        IOverclockingAdvisor overclockingAdvisor,
        IGpuOverclocker overclocker,
        IGpuDriverChecker driverChecker,
        IDlssEnablerService dlssEnabler,
        IAppSettingsService settings,
        IDialogService dialogs)
    {
        _gameMode = gameMode;
        _settings = settings;
        _dialogs = dialogs;
        _customProcessesText = string.Join(", ", settings.Load().CustomSuspendProcesses);
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        Overclocking = new OverclockingViewModel(overclockingAdvisor, overclocker, driverChecker, dialogs);
        DlssEnabler = new DlssEnablerViewModel(dlssEnabler, settings, dialogs);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    /// <summary>Sous-catégorie « Overclocking » (détection GPU + profil recommandé).</summary>
    public OverclockingViewModel Overclocking { get; }

    /// <summary>Sous-catégorie « DLSS Enabler » (gestionnaire du mod, par jeu).</summary>
    public DlssEnablerViewModel DlssEnabler { get; }

    public bool IsActive => _gameMode.IsActive;
    public string ToggleLabel => IsActive ? "Désactiver le Mode Jeu" : "Activer le Mode Jeu";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Processus suspendus lors de l'activation.</summary>
    public string SuspendedAppsList =>
        string.Join("  •  ", GameModeOptions.Default.ProcessNamesToSuspend);

    /// <summary>
    /// Applications supplémentaires à suspendre, saisies par l'utilisateur (noms de
    /// processus séparés par des virgules, sans .exe). Persistées entre les sessions.
    /// </summary>
    public string CustomProcessesText
    {
        get => _customProcessesText;
        set
        {
            if (SetProperty(ref _customProcessesText, value))
            {
                _settings.Save(_settings.Load() with
                {
                    CustomSuspendProcesses = ParseCustomProcesses(value),
                });
            }
        }
    }

    /// <summary>Découpe la saisie utilisateur en noms de processus propres (sans .exe).</summary>
    internal static string[] ParseCustomProcesses(string text) =>
        text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Options effectives : liste blanche par défaut + applications de l'utilisateur.</summary>
    private GameModeOptions BuildOptions()
    {
        var custom = ParseCustomProcesses(_customProcessesText);
        if (custom.Length == 0) return GameModeOptions.Default;

        var defaults = GameModeOptions.Default;
        return new GameModeOptions
        {
            ProcessNamesToSuspend = defaults.ProcessNamesToSuspend
                .Concat(custom)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ServiceNamesToStop = defaults.ServiceNamesToStop,
        };
    }

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
                var snap = await _gameMode.ActivateAsync(BuildOptions(), CancellationToken.None);
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
