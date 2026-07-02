using System.Collections.ObjectModel;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Option de debloat cochable dans l'UI.</summary>
public sealed class DebloatOptionViewModel : ObservableObject
{
    private bool _isSelected;
    public DebloatOptionViewModel(DebloatOption option)
    {
        Option = option;
        _isSelected = option.RecommendedDefault;
    }

    public DebloatOption Option { get; }
    public string Id          => Option.Id;
    public string Name        => Option.Name;
    public string Description => Option.Description;
    public string CategoryLabel => Option.Category switch
    {
        DebloatCategory.Telemetrie       => "Télémétrie",
        DebloatCategory.Confidentialite  => "Confidentialité",
        DebloatCategory.Interface        => "Interface",
        DebloatCategory.Applications     => "Applications",
        _ => "Autre",
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Sous-catégorie « Windows Debloat » de l'Optimisation : l'utilisateur coche les
/// actions souhaitées (anti-télémétrie, confidentialité, interface, bloatware) puis
/// les applique après confirmation.
/// </summary>
public sealed class DebloatViewModel : ObservableObject
{
    private readonly IWindowsDebloater _debloater;
    private readonly IDialogService _dialogs;

    private bool _isBusy;
    private string _status = "Cochez les optimisations souhaitées, puis cliquez sur « Appliquer la sélection ».";

    public DebloatViewModel(IWindowsDebloater debloater, IDialogService dialogs)
    {
        _debloater = debloater;
        _dialogs = dialogs;

        foreach (var opt in _debloater.GetOptions())
            Options.Add(new DebloatOptionViewModel(opt));

        ApplyCommand        = new AsyncRelayCommand(ApplyAsync, () => !IsBusy);
        SelectAllCommand    = new RelayCommand(() => SetAll(true),  () => !IsBusy);
        SelectNoneCommand   = new RelayCommand(() => SetAll(false), () => !IsBusy);
        RevertCommand       = new AsyncRelayCommand(RevertAsync, () => !IsBusy && CanRevert);
    }

    public ObservableCollection<DebloatOptionViewModel> Options { get; } = new();
    public ObservableCollection<string> ResultMessages { get; } = new();

    public AsyncRelayCommand ApplyCommand      { get; }
    public RelayCommand      SelectAllCommand  { get; }
    public RelayCommand      SelectNoneCommand { get; }
    public AsyncRelayCommand RevertCommand     { get; }

    /// <summary>Vrai si une sauvegarde existe : le bouton « Tout restaurer » est alors actif.</summary>
    public bool CanRevert => _debloater.HasBackup;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
                SelectAllCommand.RaiseCanExecuteChanged();
                SelectNoneCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void RefreshRevertState()
    {
        OnPropertyChanged(nameof(CanRevert));
        RevertCommand.RaiseCanExecuteChanged();
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string HonestNotice =>
        "⚠️ Ces réglages modifient le registre, des services et des applications Windows. " +
        "Ils sont standard et documentés, mais nécessitent les droits administrateur. Les " +
        "applications retirées restent réinstallables depuis le Microsoft Store. Une fermeture " +
        "de session ou un redémarrage peut être nécessaire pour tout appliquer.";

    private void SetAll(bool value)
    {
        foreach (var o in Options) o.IsSelected = value;
    }

    private async Task ApplyAsync()
    {
        var selected = Options.Where(o => o.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _dialogs.Info("Debloat", "Sélectionnez au moins une optimisation.");
            return;
        }

        var confirmed = _dialogs.Confirm(
            "Appliquer le debloat",
            $"{selected.Count} optimisation(s) vont être appliquées à Windows.\n\n" +
            "Ces modifications nécessitent les droits administrateur et peuvent demander " +
            "un redémarrage. Continuer ?");
        if (!confirmed) return;

        IsBusy = true;
        ResultMessages.Clear();
        Status = "Application en cours…";

        try
        {
            var progress = new Progress<string>(msg => Status = msg);
            var result = await _debloater.ApplyAsync(
                selected.Select(o => o.Id), progress, CancellationToken.None);

            foreach (var m in result.Messages) ResultMessages.Add(m);
            Status = $"Terminé : {result.Applied} appliqué(s)" +
                     (result.Failed > 0 ? $", {result.Failed} échec(s)." : ".");

            _dialogs.Info("Debloat terminé",
                $"{result.Applied} optimisation(s) appliquée(s)" +
                (result.Failed > 0 ? $", {result.Failed} échec(s)." : ".") +
                "\n\nUn redémarrage peut être nécessaire pour tout finaliser.\n" +
                "Vous pourrez tout annuler via « ↩️ Tout restaurer ».");
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Debloat", ex.Message);
            Status = "Erreur pendant l'application.";
        }
        finally
        {
            IsBusy = false;
            RefreshRevertState();
        }
    }

    private async Task RevertAsync()
    {
        var confirmed = _dialogs.Confirm(
            "Tout restaurer",
            "Restaurer l'état d'origine sauvegardé avant le debloat (valeurs de registre, " +
            "démarrage des services, tâches planifiées) ?\n\n" +
            "Note : le retrait d'applications préinstallées n'est pas annulable — " +
            "réinstallez-les depuis le Microsoft Store si besoin.");
        if (!confirmed) return;

        IsBusy = true;
        ResultMessages.Clear();
        Status = "Restauration en cours…";

        try
        {
            var progress = new Progress<string>(msg => Status = msg);
            var result = await _debloater.RevertAsync(progress, CancellationToken.None);

            foreach (var m in result.Messages) ResultMessages.Add(m);
            Status = $"Restauration terminée : {result.Applied} restauré(s)" +
                     (result.Failed > 0 ? $", {result.Failed} échec(s)." : ".");
            _dialogs.Info("Restauration terminée",
                $"{result.Applied} élément(s) restauré(s)" +
                (result.Failed > 0 ? $", {result.Failed} échec(s)." : ".") +
                "\n\nUn redémarrage peut être nécessaire.");
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Restauration", ex.Message);
            Status = "Erreur pendant la restauration.";
        }
        finally
        {
            IsBusy = false;
            RefreshRevertState();
        }
    }
}
