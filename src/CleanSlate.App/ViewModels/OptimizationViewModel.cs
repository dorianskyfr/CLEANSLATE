using System.Collections.ObjectModel;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Module 5 — Optimisation système : programmes au démarrage (sûr) + nettoyage du
/// registre (conservateur, avec SAUVEGARDE OBLIGATOIRE avant toute correction).
/// </summary>
public sealed class OptimizationViewModel : ObservableObject
{
    private readonly IStartupManager _startup;
    private readonly IRegistryCleaner _registry;
    private readonly IBackupService _backup;
    private readonly IDialogService _dialogs;

    private string _registryStatus = "Aucune analyse du registre effectuée.";

    public OptimizationViewModel(
        IStartupManager startup,
        IRegistryCleaner registry,
        IBackupService backup,
        IWindowsDebloater debloater,
        IDialogService dialogs)
    {
        _startup = startup;
        _registry = registry;
        _backup = backup;
        _dialogs = dialogs;

        Debloat = new DebloatViewModel(debloater, dialogs);

        RefreshStartupCommand = new RelayCommand(LoadStartupEntries);
        ScanRegistryCommand = new AsyncRelayCommand(ScanRegistryAsync);
        FixRegistryCommand = new AsyncRelayCommand(FixRegistryAsync,
            () => RegistryIssues.Count > 0);

        LoadStartupEntries();
    }

    /// <summary>Sous-catégorie « Windows Debloat » (anti-télémétrie, confidentialité, bloatware).</summary>
    public DebloatViewModel Debloat { get; }

    // ----------------------------------------------------------- Démarrage
    public ObservableCollection<StartupEntryViewModel> StartupEntries { get; } = new();
    public RelayCommand RefreshStartupCommand { get; }

    private void LoadStartupEntries()
    {
        StartupEntries.Clear();
        try
        {
            foreach (var entry in _startup.ListStartupEntries())
                StartupEntries.Add(new StartupEntryViewModel(entry, ToggleStartup));
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Programmes au démarrage", ex.Message);
        }
    }

    private void ToggleStartup(StartupEntryViewModel vm)
    {
        try
        {
            // On inverse l'état actuel ; SetEnabled gère le déplacement réversible.
            _startup.SetEnabled(vm.Entry, !vm.Entry.IsEnabled);
            // Rechargement pour refléter le nouvel état (les entrées sont immuables).
            LoadStartupEntries();
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Modification du démarrage",
                ex.Message + "\n\n(Les entrées système nécessitent les droits administrateur.)");
        }
    }

    // ----------------------------------------------------------- Registre
    public ObservableCollection<RegistryIssue> RegistryIssues { get; } = new();
    public AsyncRelayCommand ScanRegistryCommand { get; }
    public AsyncRelayCommand FixRegistryCommand { get; }

    public string RegistryStatus
    {
        get => _registryStatus;
        private set => SetProperty(ref _registryStatus, value);
    }

    public string RegistryHonestNotice =>
        "⚠️ Nettoyer le registre n'apporte quasiment aucun gain de performance et " +
        "comporte un risque. CleanSlate se limite aux entrées de démarrage pointant " +
        "vers un programme qui n'existe plus, et CRÉE TOUJOURS une sauvegarde (.reg) " +
        "restaurable avant toute suppression.";

    private async Task ScanRegistryAsync()
    {
        RegistryStatus = "Analyse du registre en cours…";
        try
        {
            RegistryIssues.Clear();
            var issues = await _registry.ScanAsync(CancellationToken.None);
            foreach (var i in issues) RegistryIssues.Add(i);

            RegistryStatus = issues.Count == 0
                ? "Aucune entrée orpheline détectée. 🎉"
                : $"{issues.Count} entrée(s) orpheline(s) détectée(s).";
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Analyse du registre", ex.Message);
            RegistryStatus = "Erreur pendant l'analyse.";
        }
        finally
        {
            FixRegistryCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task FixRegistryAsync()
    {
        if (RegistryIssues.Count == 0) return;

        var confirmed = _dialogs.Confirm(
            "Corriger le registre",
            $"{RegistryIssues.Count} entrée(s) vont être supprimées.\n\n" +
            "Une sauvegarde (.reg) sera créée AVANT toute modification et pourra être " +
            "restaurée. Continuer ?");
        if (!confirmed) return;

        try
        {
            // SAUVEGARDE OBLIGATOIRE des deux clés Run avant toute suppression.
            // Si la sauvegarde échoue, l'exception interrompt tout : rien n'est modifié.
            const string hkcuRun = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
            const string hklmRun = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run";

            var backupPath = await _backup.CreateBackupAsync(hkcuRun, CancellationToken.None);
            try { await _backup.CreateBackupAsync(hklmRun, CancellationToken.None); }
            catch { /* HKLM peut nécessiter l'admin ; HKCU suffit comme garde-fou */ }

            var fixedCount = await _registry.FixAsync(
                RegistryIssues.ToList(), backupPath, CancellationToken.None);

            RegistryIssues.Clear();
            RegistryStatus = $"{fixedCount} entrée(s) supprimée(s). Sauvegarde : {backupPath}";
            _dialogs.Info("Nettoyage du registre",
                $"{fixedCount} entrée(s) supprimée(s).\nSauvegarde restaurable :\n{backupPath}");
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Nettoyage du registre",
                "Aucune modification effectuée.\n\n" + ex.Message);
            RegistryStatus = "Correction annulée (sauvegarde impossible ou erreur).";
        }
        finally
        {
            FixRegistryCommand.RaiseCanExecuteChanged();
        }
    }
}
