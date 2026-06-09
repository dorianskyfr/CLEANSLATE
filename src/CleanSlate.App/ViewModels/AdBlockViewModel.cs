using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

public sealed class AdBlockViewModel : ObservableObject
{
    private readonly IAdBlockService _adBlock;
    private readonly IDialogService _dialogs;
    private string _status = string.Empty;
    private bool _isBusy;

    public AdBlockViewModel(IAdBlockService adBlock, IDialogService dialogs)
    {
        _adBlock = adBlock;
        _dialogs = dialogs;

        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy);
        UpdateListCommand = new AsyncRelayCommand(UpdateListAsync, () => !IsBusy);

        RefreshState();
    }

    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand UpdateListCommand { get; }

    public bool IsEnabled => _adBlock.IsEnabled;
    public int BlockedDomainCount => _adBlock.BlockedDomainCount;
    public string ToggleLabel => IsEnabled ? "Désactiver AdBlock" : "Activer AdBlock";
    public string BlockedCountText => IsEnabled
        ? $"{BlockedDomainCount:N0} domaines bloqués"
        : "Inactif";

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ToggleCommand.RaiseCanExecuteChanged();
                UpdateListCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => Status = msg);
            if (IsEnabled)
            {
                Status = "Désactivation…";
                await _adBlock.DisableAsync(CancellationToken.None);
                Status = "AdBlock désactivé.";
            }
            else
            {
                await _adBlock.EnableAsync(progress, CancellationToken.None);
                Status = $"AdBlock activé — {_adBlock.BlockedDomainCount:N0} domaines bloqués.";
            }
        }
        catch (UnauthorizedAccessException)
        {
            Status = string.Empty;
            _dialogs.Warn("Droits insuffisants",
                "La modification du fichier hosts requiert des droits administrateur.\n" +
                "Relancez CleanSlate en tant qu'administrateur.");
        }
        catch (Exception ex)
        {
            Status = string.Empty;
            _dialogs.Warn("Erreur AdBlock", ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    private async Task UpdateListAsync()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(msg => Status = msg);
            await _adBlock.UpdateListAsync(progress, CancellationToken.None);
            Status = $"Liste mise à jour — {_adBlock.BlockedDomainCount:N0} domaines.";
        }
        catch (Exception ex)
        {
            Status = string.Empty;
            _dialogs.Warn("Erreur de mise à jour", ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(BlockedDomainCount));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(BlockedCountText));
    }
}
