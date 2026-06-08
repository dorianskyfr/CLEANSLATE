using System.Windows.Threading;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Module 3 — Surveillance de la RAM en temps réel + optimisation avancée.</summary>
public sealed class MemoryViewModel : ObservableObject
{
    private readonly IMemoryMonitor _monitor;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;

    private string _totalDisplay     = "—";
    private string _usedDisplay      = "—";
    private string _availableDisplay = "—";
    private double _loadPercent;
    private string _lastResult       = string.Empty;
    private bool   _isBusy;

    public MemoryViewModel(IMemoryMonitor monitor, IDialogService dialogs)
    {
        _monitor = monitor;
        _dialogs = dialogs;

        OptimizeCommand = new AsyncRelayCommand(OptimizeAsync, () => !IsBusy);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
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

    public string HonestNotice =>
        "Sur Windows moderne, « vider la RAM » est généralement inutile voire contre-productif : " +
        "le cache mémoire est bénéfique. L'optimisation avancée purge aussi la Standby List " +
        "(mémoire non utilisée en cache) ce qui libère de la RAM physique réelle — mais l'effet " +
        "est temporaire. Requiert les droits administrateur pour la Standby List.";

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
        var confirmed = _dialogs.Confirm(
            "Optimiser la RAM",
            HonestNotice + "\n\nLancer l'optimisation mémoire ?");
        if (!confirmed) return;

        IsBusy = true;
        LastResult = "Optimisation en cours…";

        var result = await Task.Run(() => _monitor.OptimizeMemory(clearStandbyList: true));

        Refresh();
        IsBusy = false;
        LastResult = result.Message;
    }
}
