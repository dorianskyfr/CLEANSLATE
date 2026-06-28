using System.Management;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

public sealed class AdBlockViewModel : ObservableObject
{
    private readonly IAdBlockService _adBlock;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private string _status = string.Empty;
    private bool _isBusy;
    private DnsProviderOption _selectedProvider;

    public AdBlockViewModel(IAdBlockService adBlock, IAppSettingsService settings, IDialogService dialogs)
    {
        _adBlock = adBlock;
        _settings = settings;
        _dialogs = dialogs;

        var savedId = settings.Load().AdBlockProvider;
        _selectedProvider = adBlock.Providers.FirstOrDefault(p => p.Id == savedId)
            ?? adBlock.Providers[0];

        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy);

        RefreshState();
    }

    public AsyncRelayCommand ToggleCommand { get; }

    public IReadOnlyList<DnsProviderOption> Providers => _adBlock.Providers;

    /// <summary>Fournisseur DNS filtrant choisi (persisté entre les sessions).</summary>
    public DnsProviderOption SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            // Ne jamais écrire un fournisseur null dans le champ (un ComboBox vidé
            // ferait planter ProviderDescription) : on ignore les valeurs nulles.
            if (value is null) return;
            if (SetProperty(ref _selectedProvider, value))
            {
                OnPropertyChanged(nameof(ProviderDescription));
                _settings.Save(_settings.Load() with { AdBlockProvider = value.Id });
            }
        }
    }

    public string ProviderDescription => _selectedProvider.Description;

    public bool IsEnabled => _adBlock.IsEnabled;

    /// <summary>Le fournisseur ne se change que blocage désactivé (désactiver → changer → réactiver).</summary>
    public bool CanChooseProvider => !IsEnabled;

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
                var provider = SelectedProvider;
                await _adBlock.EnableAsync(provider, progress, CancellationToken.None);
                Status = $"DNS {provider.Name} activé ({provider.Primary} / {provider.Secondary}).";
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
        OnPropertyChanged(nameof(CanChooseProvider));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(StatusDetails));
    }
}
