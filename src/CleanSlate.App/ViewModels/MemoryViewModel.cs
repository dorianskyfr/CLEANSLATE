using System.Windows.Threading;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Module 3 — Surveillance de la RAM en temps réel. Un timer rafraîchit l'affichage
/// chaque seconde. La « libération » est proposée AVEC un avertissement honnête.
/// </summary>
public sealed class MemoryViewModel : ObservableObject
{
    private readonly IMemoryMonitor _monitor;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;

    private string _totalDisplay = "—";
    private string _usedDisplay = "—";
    private string _availableDisplay = "—";
    private double _loadPercent;

    public MemoryViewModel(IMemoryMonitor monitor, IDialogService dialogs)
    {
        _monitor = monitor;
        _dialogs = dialogs;

        FreeMemoryCommand = new RelayCommand(FreeMemory);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public RelayCommand FreeMemoryCommand { get; }

    public string TotalDisplay { get => _totalDisplay; private set => SetProperty(ref _totalDisplay, value); }
    public string UsedDisplay { get => _usedDisplay; private set => SetProperty(ref _usedDisplay, value); }
    public string AvailableDisplay { get => _availableDisplay; private set => SetProperty(ref _availableDisplay, value); }
    public double LoadPercent { get => _loadPercent; private set => SetProperty(ref _loadPercent, value); }

    /// <summary>Avertissement permanent affiché dans la vue (transparence).</summary>
    public string HonestNotice =>
        "Sur Windows moderne, « libérer la RAM » est généralement inutile, voire " +
        "contre-productif : la mémoire est seulement repoussée vers le disque et " +
        "rechargée ensuite. Windows gère déjà très bien la mémoire. Fonction fournie " +
        "à titre de diagnostic.";

    private void Refresh()
    {
        var snap = _monitor.Read();
        TotalDisplay = FileActionLogger.FormatBytes((long)snap.TotalPhysicalBytes);
        UsedDisplay = FileActionLogger.FormatBytes((long)snap.UsedPhysicalBytes);
        AvailableDisplay = FileActionLogger.FormatBytes((long)snap.AvailablePhysicalBytes);
        LoadPercent = snap.MemoryLoadPercent;
    }

    private void FreeMemory()
    {
        var confirmed = _dialogs.Confirm(
            "Libérer la RAM (déconseillé)",
            HonestNotice + "\n\nVoulez-vous tout de même tenter la libération ?");
        if (!confirmed) return;

        var count = _monitor.TryFreeMemory();
        Refresh();
        _dialogs.Info("Libération de la RAM",
            $"{count} processus traités. Effet probablement marginal ou temporaire — " +
            "c'est le comportement normal sur Windows.");
    }
}
