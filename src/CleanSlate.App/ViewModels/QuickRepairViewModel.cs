using System.Collections.ObjectModel;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Module 6 — Réparation rapide : checkup + corrections automatiques.</summary>
public sealed class QuickRepairViewModel : ObservableObject
{
    private readonly IQuickRepairService _service;
    private readonly IDialogService _dialogs;

    private bool _isBusy;
    private string _statusMessage = "Cliquez sur « Lancer le diagnostic » pour analyser votre système.";
    private double _progress;

    public QuickRepairViewModel(IQuickRepairService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        RunDiagnosticCommand = new AsyncRelayCommand(RunDiagnosticAsync, () => !IsBusy);
        Checks = new ObservableCollection<RepairCheck>();
    }

    public ObservableCollection<RepairCheck> Checks { get; }
    public AsyncRelayCommand RunDiagnosticCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RunDiagnosticCommand.RaiseCanExecuteChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public Task RepairCheckAsync(RepairCheck check) => RepairAsync(check);

    // -----------------------------------------------------------------------

    private async Task RunDiagnosticAsync()
    {
        IsBusy = true;
        Progress = 0;
        Checks.Clear();

        var allChecks = await _service.CreateChecksAsync();
        foreach (var c in allChecks) Checks.Add(c);

        StatusMessage = "Analyse en cours…";
        int done = 0;
        foreach (var check in Checks)
        {
            await _service.RunCheckAsync(check, CancellationToken.None);
            done++;
            Progress = (double)done / Checks.Count * 100;
        }

        int errors   = Checks.Count(c => c.Status == RepairStatus.Error);
        int warnings = Checks.Count(c => c.Status == RepairStatus.Warning);
        StatusMessage = (errors + warnings) == 0
            ? "Tout est en ordre. ✅"
            : $"Diagnostic terminé : {errors} erreur(s), {warnings} avertissement(s).";

        IsBusy = false;
        Progress = 100;
    }

    internal async Task RepairAsync(RepairCheck check)
    {
        if (!check.CanRepair) return;

        var confirmed = _dialogs.Confirm(
            $"Réparer : {check.Name}",
            $"Lancer la réparation automatique pour « {check.Name} » ?\n\n{check.Detail}");
        if (!confirmed) return;

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            await _service.RepairAsync(check, progress, CancellationToken.None);
            StatusMessage = $"Réparation de « {check.Name} » terminée : {check.Detail}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec de la réparation de « {check.Name} » : {ex.Message}";
            _dialogs.Warn("Réparation rapide", ex.Message);
        }
        finally
        {
            IsBusy = false; // toujours réinitialisé, même si la réparation échoue
        }
    }
}
