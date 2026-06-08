using System.Collections.ObjectModel;
using System.Windows.Input;
using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Models;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// ViewModel du module 1 (nettoyage). Pilote les deux phases distinctes :
/// ANALYSER (aperçu, ne supprime rien) puis NETTOYER (après confirmation).
/// </summary>
public sealed class CleaningViewModel : ObservableObject
{
    private readonly CleaningEngine _engine;
    private readonly IDialogService _dialogs;

    private string _statusMessage = "Cliquez sur « Analyser » pour estimer l'espace récupérable.";
    private bool _isBusy;
    private double _progressValue;
    private long _totalRecoverableBytes;
    private CancellationTokenSource? _cts;

    public CleaningViewModel(CleaningEngine engine, IDialogService dialogs)
    {
        _engine = engine;
        _dialogs = dialogs;

        Categories = new ObservableCollection<CategoryViewModel>(
            _engine.Providers.Select(p => new CategoryViewModel(p)));

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, CanClean);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
    }

    public ObservableCollection<CategoryViewModel> Categories { get; }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                CleanCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public long TotalRecoverableBytes
    {
        get => _totalRecoverableBytes;
        private set
        {
            if (SetProperty(ref _totalRecoverableBytes, value))
                OnPropertyChanged(nameof(TotalRecoverableDisplay));
        }
    }

    public string TotalRecoverableDisplay => FileActionLogger.FormatBytes(TotalRecoverableBytes);

    // =====================================================================
    //  PHASE 1 : ANALYSER — ne supprime rien.
    // =====================================================================
    private async Task ScanAsync()
    {
        var selected = Categories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _dialogs.Info("Analyse", "Sélectionnez au moins une catégorie à analyser.");
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Analyse en cours…";
        _cts = new CancellationTokenSource();

        foreach (var c in selected) c.Reset();

        var progress = new Progress<ScanProgress>(p =>
            StatusMessage = $"Analyse : {p.ProviderDisplayName}…");

        try
        {
            var results = await _engine.ScanAllAsync(
                selected.Select(c => c.Provider.Id), progress, _cts.Token);

            foreach (var result in results)
            {
                var vm = Categories.First(c => c.Provider.Id == result.ProviderId);
                vm.ApplyScanResult(result);
            }

            TotalRecoverableBytes = selected.Sum(c => c.RecoverableBytes);
            var itemCount = selected.Sum(c => c.ItemCount);
            StatusMessage = itemCount == 0
                ? "Rien à nettoyer : votre système est déjà propre. 🎉"
                : $"Analyse terminée : {itemCount} élément(s), {TotalRecoverableDisplay} récupérable(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analyse annulée.";
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Erreur d'analyse", ex.Message);
            StatusMessage = "Erreur pendant l'analyse.";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            CleanCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanClean() =>
        !IsBusy && Categories.Any(c => c.IsSelected && c.HasScanned && c.ItemCount > 0);

    // =====================================================================
    //  PHASE 2 : NETTOYER — uniquement après aperçu + confirmation explicite.
    // =====================================================================
    private async Task CleanAsync()
    {
        // On ne nettoie QUE ce qui a été scanné et sélectionné.
        var items = Categories
            .Where(c => c.IsSelected && c.HasScanned)
            .SelectMany(c => c.ScannedItems)
            .ToList();

        if (items.Count == 0)
        {
            _dialogs.Info("Nettoyage", "Lancez d'abord une analyse.");
            return;
        }

        var totalSize = items.Sum(i => i.SizeBytes);

        // CONFIRMATION OBLIGATOIRE avant toute suppression.
        var confirmed = _dialogs.Confirm(
            "Confirmer le nettoyage",
            $"{items.Count} élément(s) vont être supprimés définitivement " +
            $"({FileActionLogger.FormatBytes(totalSize)}).\n\n" +
            "Cette action est irréversible. Continuer ?");

        if (!confirmed)
        {
            StatusMessage = "Nettoyage annulé.";
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();

        var progress = new Progress<CleanProgress>(p =>
        {
            ProgressValue = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
            StatusMessage = $"Nettoyage : {p.Processed}/{p.Total}…";
        });

        try
        {
            var result = await _engine.CleanAsync(items, progress, _cts.Token);

            // On rafraîchit l'aperçu pour refléter ce qui reste (fichiers verrouillés).
            foreach (var c in Categories.Where(c => c.IsSelected)) c.Reset();
            TotalRecoverableBytes = 0;

            var msg = $"Nettoyage terminé : {result.DeletedCount} supprimé(s), " +
                      $"{FileActionLogger.FormatBytes(result.FreedBytes)} libéré(s).";
            if (result.FailedCount > 0)
                msg += $" {result.FailedCount} élément(s) ignoré(s) (verrouillés ou protégés).";

            StatusMessage = msg;
            _dialogs.Info("Nettoyage terminé", msg);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Nettoyage annulé.";
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Erreur de nettoyage", ex.Message);
            StatusMessage = "Erreur pendant le nettoyage.";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
    }
}
