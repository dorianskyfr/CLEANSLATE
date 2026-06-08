using System.Collections.ObjectModel;
using System.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Module 2 — Inventaire des pilotes.
///
/// RAPPEL HONNÊTE : on N'AFFICHE PAS de « dernière version disponible » inventée.
/// On liste les pilotes installés (fiable) et on offre des raccourcis légitimes :
/// rechercher des mises à jour via Windows Update, ou ouvrir une recherche
/// constructeur. Voir docs/LIMITES-TECHNIQUES.md.
/// </summary>
public sealed class DriversViewModel : ObservableObject
{
    private readonly IDriverInventory _inventory;
    private readonly IDialogService _dialogs;
    private bool _isLoading;
    private DriverInfo? _selected;

    public DriversViewModel(IDriverInventory inventory, IDialogService dialogs)
    {
        _inventory = inventory;
        _dialogs = dialogs;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        OpenWindowsUpdateCommand = new RelayCommand(OpenWindowsUpdate);
        SearchManufacturerCommand = new RelayCommand(SearchManufacturer, () => Selected is not null);
    }

    public ObservableCollection<DriverInfo> Drivers { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenWindowsUpdateCommand { get; }
    public RelayCommand SearchManufacturerCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (SetProperty(ref _isLoading, value)) RefreshCommand.RaiseCanExecuteChanged(); }
    }

    public DriverInfo? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) SearchManufacturerCommand.RaiseCanExecuteChanged(); }
    }

    public string HonestNotice =>
        "Il n'existe pas d'API universelle et gratuite donnant « la dernière version » " +
        "d'un pilote. CleanSlate liste vos pilotes installés et vous oriente vers des " +
        "sources fiables (Windows Update, sites constructeurs). Évitez les « driver " +
        "updaters » tiers, vecteur fréquent de logiciels indésirables.";

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Drivers.Clear();
            // L'inventaire WMI peut être lent : on l'exécute hors du thread UI.
            var list = await Task.Run(() => _inventory.ListInstalledDrivers());
            foreach (var d in list) Drivers.Add(d);
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Inventaire des pilotes", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenWindowsUpdate()
    {
        // Ouvre la page « Mises à jour facultatives » (pilotes) de Windows.
        TryOpen("ms-settings:windowsupdate-optionalupdates");
    }

    private void SearchManufacturer()
    {
        if (Selected is null) return;
        var query = Uri.EscapeDataString(
            $"{Selected.Manufacturer} {Selected.DeviceName} pilote Windows");
        TryOpen($"https://www.bing.com/search?q={query}");
    }

    private void TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Ouverture impossible", ex.Message);
        }
    }
}
