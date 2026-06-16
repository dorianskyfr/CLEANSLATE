using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Une application à cocher dans la liste de suspension du Mode Jeu. Notifie le
/// ViewModel parent quand l'utilisateur (dé)coche, pour persister la sélection.
/// </summary>
public sealed class SuspendAppItem : ObservableObject
{
    private readonly Action _onChanged;
    private bool _isChecked;

    public SuspendAppItem(SuspendableApp app, bool isChecked, Action onChanged)
    {
        App = app;
        _isChecked = isChecked;
        _onChanged = onChanged;
    }

    public SuspendableApp App { get; }
    public string ProcessName => App.ProcessName;
    public string DisplayName => App.DisplayName;
    public string Category => App.Category;
    public string Note => App.Note;

    /// <summary>Vrai si la note contient un avertissement (préfixe ⚠️) — pour la teinter.</summary>
    public bool IsWarning => App.Note.StartsWith("⚠️", StringComparison.Ordinal);

    public bool IsChecked
    {
        get => _isChecked;
        set { if (SetProperty(ref _isChecked, value)) _onChanged(); }
    }
}

/// <summary>
/// Module 4 — Mode Jeu. Bascule l'activation/désactivation, avec restauration
/// systématique. La liste des applications à suspendre est désormais une liste à
/// cocher (groupée par catégorie) pilotée par des profils prêts à l'emploi
/// (Léger / Équilibré / Agressif), au lieu d'un champ de texte.
/// </summary>
public sealed class GameModeViewModel : ObservableObject
{
    private readonly IGameMode _gameMode;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private string _status = "Mode Jeu inactif.";
    private string _activeProfile;
    private bool _suppressPersist;

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

        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        ApplyLightProfileCommand      = new RelayCommand(() => ApplyProfile(SuspendTier.Leger));
        ApplyBalancedProfileCommand   = new RelayCommand(() => ApplyProfile(SuspendTier.Equilibre));
        ApplyAggressiveProfileCommand = new RelayCommand(() => ApplyProfile(SuspendTier.Agressif));

        Overclocking  = new OverclockingViewModel(overclockingAdvisor, overclocker, driverChecker, dialogs);
        DlssEnabler   = new DlssEnablerViewModel(dlssEnabler, settings, dialogs);

        var loaded = settings.Load();
        _activeProfile = loaded.SuspendProfile;

        // Sélection initiale : reprise de la sélection sauvegardée, sinon profil par défaut.
        var initial = loaded.SuspendSelection is { Count: > 0 }
            ? new HashSet<string>(loaded.SuspendSelection, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(SuspendCatalog.ProcessesFor(ParseTier(loaded.SuspendProfile)), StringComparer.OrdinalIgnoreCase);

        foreach (var app in SuspendCatalog.Apps)
            SuspendApps.Add(new SuspendAppItem(app, initial.Contains(app.ProcessName), PersistSelection));

        AppsView = CollectionViewSource.GetDefaultView(SuspendApps);
        AppsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SuspendAppItem.Category)));
    }

    public AsyncRelayCommand ToggleCommand { get; }
    public RelayCommand ApplyLightProfileCommand { get; }
    public RelayCommand ApplyBalancedProfileCommand { get; }
    public RelayCommand ApplyAggressiveProfileCommand { get; }

    /// <summary>Sous-catégorie « Overclocking » (détection GPU + profil recommandé).</summary>
    public OverclockingViewModel Overclocking { get; }

    /// <summary>Sous-catégorie « DLSS Enabler » (gestionnaire du mod, par jeu).</summary>
    public DlssEnablerViewModel DlssEnabler { get; }

    /// <summary>Applications à cocher (groupées par catégorie dans la vue).</summary>
    public ObservableCollection<SuspendAppItem> SuspendApps { get; } = new();
    public ICollectionView AppsView { get; }

    public bool IsActive => _gameMode.IsActive;
    public string ToggleLabel => IsActive ? "Désactiver le Mode Jeu" : "Activer le Mode Jeu";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Nombre d'applications actuellement cochées (affiché à côté du bouton).</summary>
    public string SelectionSummary
    {
        get
        {
            var n = SuspendApps.Count(a => a.IsChecked);
            return n == 0
                ? "Aucune application sélectionnée — le Mode Jeu n'arrêtera que les services Windows."
                : $"{n} application(s) seront suspendues, puis reprises à la désactivation.";
        }
    }

    /// <summary>Services arrêtés lors de l'activation.</summary>
    public string StoppedServicesList =>
        string.Join("  •  ", GameModeOptions.Default.ServiceNamesToStop);

    public string HonestNotice =>
        "Le Mode Jeu suspend (sans les fermer) les applications cochées ci-dessous et " +
        "arrête temporairement quelques services Windows, puis restaure tout à la " +
        "désactivation. Discord n'est jamais touché (vocal possible). Choisissez un " +
        "profil prêt à l'emploi, puis ajustez la liste à la coche selon vos habitudes. " +
        "En cas de fermeture brutale, l'état est restauré au prochain démarrage.";

    private static SuspendTier ParseTier(string name) => name switch
    {
        "Leger"    => SuspendTier.Leger,
        "Agressif" => SuspendTier.Agressif,
        _          => SuspendTier.Equilibre,
    };

    /// <summary>Applique un profil : coche les apps de ce niveau, décoche les autres.</summary>
    private void ApplyProfile(SuspendTier tier)
    {
        var inProfile = new HashSet<string>(SuspendCatalog.ProcessesFor(tier), StringComparer.OrdinalIgnoreCase);

        _suppressPersist = true;
        foreach (var item in SuspendApps)
            item.IsChecked = inProfile.Contains(item.ProcessName);
        _suppressPersist = false;

        _activeProfile = tier.ToString();
        PersistSelection();
        Status = $"Profil « {ProfileLabel(tier)} » appliqué : {SuspendApps.Count(a => a.IsChecked)} application(s) sélectionnée(s).";
    }

    private static string ProfileLabel(SuspendTier tier) => tier switch
    {
        SuspendTier.Leger    => "Léger",
        SuspendTier.Agressif => "Agressif",
        _                    => "Équilibré",
    };

    /// <summary>Persiste la sélection cochée + le profil actif dans les préférences.</summary>
    private void PersistSelection()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        if (_suppressPersist) return;

        var selection = SuspendApps.Where(a => a.IsChecked).Select(a => a.ProcessName).ToArray();
        _settings.Save(_settings.Load() with
        {
            SuspendSelection = selection,
            SuspendProfile = _activeProfile,
        });
    }

    /// <summary>Options effectives : applications cochées + services Windows par défaut.</summary>
    private GameModeOptions BuildOptions()
    {
        var processes = SuspendApps.Where(a => a.IsChecked).Select(a => a.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GameModeOptions
        {
            ProcessNamesToSuspend = processes,
            ServiceNamesToStop = GameModeOptions.Default.ServiceNamesToStop,
        };
    }

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
