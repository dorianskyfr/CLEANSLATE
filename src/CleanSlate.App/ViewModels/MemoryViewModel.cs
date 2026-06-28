using System.Windows.Threading;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Module 3 — Surveillance de la RAM en temps réel + optimisation avancée.</summary>
public sealed class MemoryViewModel : ObservableObject
{
    /// <summary>Délai minimal entre deux optimisations automatiques.</summary>
    private static readonly TimeSpan AutoOptimizeCooldown = TimeSpan.FromMinutes(10);

    private readonly IMemoryMonitor _monitor;
    private readonly IAppSettingsService _settings;
    private readonly DispatcherTimer _timer;

    private string _totalDisplay     = "—";
    private string _usedDisplay      = "—";
    private string _availableDisplay = "—";
    private double _loadPercent;
    private string _lastResult       = string.Empty;
    private bool   _isBusy;
    private bool   _autoOptimizeEnabled;
    private int    _autoThreshold = 90;
    private DateTime _lastAutoOptimize = DateTime.MinValue;

    public MemoryViewModel(IMemoryMonitor monitor, IAppSettingsService settings, IDialogService dialogs)
    {
        _monitor = monitor;
        _settings = settings;

        var saved = settings.Load();
        _autoOptimizeEnabled = saved.AutoMemoryOptimize;
        _autoThreshold = Math.Clamp(saved.AutoMemoryOptimizeThreshold, 50, 99);

        OptimizeCommand = new AsyncRelayCommand(OptimizeAsync, () => !IsBusy);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            // Un échec transitoire de lecture mémoire ne doit jamais faire planter
            // l'application via une exception non observée d'un gestionnaire async void.
            try
            {
                Refresh();
                await MaybeAutoOptimizeAsync();
            }
            catch { /* tick best-effort : on réessaie à la seconde suivante */ }
        };
        _timer.Start();
        Refresh();
    }

    public AsyncRelayCommand OptimizeCommand { get; }

    public string TotalDisplay     { get => _totalDisplay;     private set => SetProperty(ref _totalDisplay, value); }
    public string UsedDisplay      { get => _usedDisplay;      private set => SetProperty(ref _usedDisplay, value); }
    public string AvailableDisplay { get => _availableDisplay; private set => SetProperty(ref _availableDisplay, value); }
    public double LoadPercent      { get => _loadPercent;      private set => SetProperty(ref _loadPercent, value); }
    public string LastResult       { get => _lastResult;       private set => SetProperty(ref _lastResult, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) OptimizeCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Optimisation automatique quand la charge mémoire dépasse le seuil (persisté).</summary>
    public bool AutoOptimizeEnabled
    {
        get => _autoOptimizeEnabled;
        set
        {
            if (SetProperty(ref _autoOptimizeEnabled, value))
            {
                OnPropertyChanged(nameof(AutoOptimizeLabel));
                _settings.Save(_settings.Load() with { AutoMemoryOptimize = value });
            }
        }
    }

    public string AutoOptimizeLabel =>
        $"Optimiser automatiquement quand la RAM dépasse {_autoThreshold} % " +
        "(au plus une fois toutes les 10 minutes)";

    public string HonestNotice =>
        "L'optimisation compacte la mémoire des processus (working sets) et purge la " +
        "Standby List (mémoire en cache non utilisée), ce qui libère immédiatement de la " +
        "RAM physique — comme Wise Memory Optimizer. Idéal avant de lancer un jeu ou une " +
        "application gourmande. La purge de la Standby List requiert les droits administrateur.";

    /// <summary>
    /// Optimisation automatique : déclenchée par le timer d'affichage quand la charge
    /// dépasse le seuil, avec un délai minimal entre deux passages (pas de spam).
    /// </summary>
    private async Task MaybeAutoOptimizeAsync()
    {
        if (!_autoOptimizeEnabled || IsBusy) return;
        if (LoadPercent < _autoThreshold) return;
        if (DateTime.UtcNow - _lastAutoOptimize < AutoOptimizeCooldown) return;

        _lastAutoOptimize = DateTime.UtcNow;
        IsBusy = true;
        LastResult = $"RAM > {_autoThreshold} % — optimisation automatique en cours…";

        try
        {
            var result = await Task.Run(() => _monitor.OptimizeMemory(clearStandbyList: true));
            LastResult = $"[Auto] {result.Message}";
        }
        catch (Exception ex)
        {
            LastResult = $"[Auto] Échec de l'optimisation : {ex.Message}";
        }
        finally
        {
            Refresh();
            IsBusy = false; // toujours réinitialisé, sinon le bouton resterait grisé à jamais
        }
    }

    private void Refresh()
    {
        var snap = _monitor.Read();
        TotalDisplay     = FileActionLogger.FormatBytes((long)snap.TotalPhysicalBytes);
        UsedDisplay      = FileActionLogger.FormatBytes((long)snap.UsedPhysicalBytes);
        AvailableDisplay = FileActionLogger.FormatBytes((long)snap.AvailablePhysicalBytes);
        LoadPercent      = snap.MemoryLoadPercent;
    }

    private async Task OptimizeAsync()
    {
        // Pas de fenêtre de confirmation : un clic = optimisation immédiate,
        // le résultat s'affiche directement dans la page.
        IsBusy = true;
        LastResult = "Optimisation en cours…";

        try
        {
            var result = await Task.Run(() => _monitor.OptimizeMemory(clearStandbyList: true));
            LastResult = result.Message;
        }
        catch (Exception ex)
        {
            LastResult = $"Échec de l'optimisation : {ex.Message}";
        }
        finally
        {
            Refresh();
            IsBusy = false; // toujours réinitialisé, sinon le bouton resterait grisé à jamais
        }
    }
}
