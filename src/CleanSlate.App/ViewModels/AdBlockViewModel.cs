using System.Management;
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

        RefreshState();
    }

    public AsyncRelayCommand ToggleCommand { get; }

    public bool IsEnabled => _adBlock.IsEnabled;
    public string ToggleLabel => IsEnabled ? "Désactiver le blocage de pub (DNS)" : "Activer le blocage de pub (DNS)";
    public string StatusDetails => _adBlock.StatusDetails;

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
                ToggleCommand.RaiseCanExecuteChanged();
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
                await _adBlock.DisableAsync(progress, CancellationToken.None);
                Status = "DNS système restauré — blocage de pub désactivé.";
            }
            else
            {
                await _adBlock.EnableAsync(progress, CancellationToken.None);
                Status = $"DNS AdGuard activé ({DnsAdBlockService.PrimaryDns} / {DnsAdBlockService.SecondaryDns}).";
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            Status = string.Empty;
            _dialogs.Warn("Droits insuffisants",
                "La modification de la configuration DNS requiert des droits administrateur.\n" +
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

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(StatusDetails));
    }
}
