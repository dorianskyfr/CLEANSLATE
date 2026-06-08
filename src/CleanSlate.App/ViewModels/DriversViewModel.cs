using System.Collections.ObjectModel;
using System.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Info sur une mise à jour de pilote disponible via Windows Update.</summary>
public sealed record DriverUpdateInfo(string Title, long SizeBytes, bool IsDownloaded)
{
    public string SizeDisplay => FormatBytes(SizeBytes);
    private static string FormatBytes(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}

/// <summary>Module 2 — Inventaire pilotes + recherche WUApi de mises à jour.</summary>
public sealed class DriversViewModel : ObservableObject
{
    private readonly IDriverInventory _inventory;
    private readonly IDialogService   _dialogs;

    private bool       _isLoading;
    private bool       _isSearchingUpdates;
    private DriverInfo? _selected;
    private string     _updateStatus = "Cliquez sur « Chercher les mises à jour » pour interroger Windows Update.";

    public DriversViewModel(IDriverInventory inventory, IDialogService dialogs)
    {
        _inventory = inventory;
        _dialogs   = dialogs;

        RefreshCommand            = new AsyncRelayCommand(LoadAsync,                () => !IsLoading && !IsSearchingUpdates);
        SearchDriverUpdatesCommand = new AsyncRelayCommand(SearchDriverUpdatesAsync, () => !IsLoading && !IsSearchingUpdates);
        InstallUpdateCommand       = new AsyncRelayCommand(InstallSelectedUpdateAsync, () => SelectedUpdate is not null && !IsSearchingUpdates);
        OpenWindowsUpdateCommand  = new RelayCommand(OpenWindowsUpdate);
        SearchManufacturerCommand = new RelayCommand(SearchManufacturer, () => Selected is not null);
    }

    public ObservableCollection<DriverInfo>       Drivers       { get; } = new();
    public ObservableCollection<DriverUpdateInfo> DriverUpdates { get; } = new();

    public AsyncRelayCommand RefreshCommand             { get; }
    public AsyncRelayCommand SearchDriverUpdatesCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand       { get; }
    public RelayCommand      OpenWindowsUpdateCommand   { get; }
    public RelayCommand      SearchManufacturerCommand  { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (SetProperty(ref _isLoading, value)) RaiseAll(); }
    }

    public bool IsSearchingUpdates
    {
        get => _isSearchingUpdates;
        private set { if (SetProperty(ref _isSearchingUpdates, value)) RaiseAll(); }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public DriverInfo? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) SearchManufacturerCommand.RaiseCanExecuteChanged(); }
    }

    private DriverUpdateInfo? _selectedUpdate;
    public DriverUpdateInfo? SelectedUpdate
    {
        get => _selectedUpdate;
        set { if (SetProperty(ref _selectedUpdate, value)) InstallUpdateCommand.RaiseCanExecuteChanged(); }
    }

    private void RaiseAll()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        SearchDriverUpdatesCommand.RaiseCanExecuteChanged();
        InstallUpdateCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Drivers.Clear();
            var list = await Task.Run(() => _inventory.ListInstalledDrivers());
            foreach (var d in list) Drivers.Add(d);
        }
        catch (Exception ex) { _dialogs.Warn("Inventaire des pilotes", ex.Message); }
        finally { IsLoading = false; }
    }

    // ---- Recherche WUApi ----

    private async Task SearchDriverUpdatesAsync()
    {
        IsSearchingUpdates = true;
        UpdateStatus = "Interrogation de Windows Update (peut prendre 1-2 minutes)…";
        DriverUpdates.Clear();

        try
        {
            var updates = await Task.Run(() => FindDriverUpdatesViaWuApi());
            foreach (var u in updates) DriverUpdates.Add(u);
            UpdateStatus = updates.Count == 0
                ? "Tous les pilotes sont à jour. ✅"
                : $"{updates.Count} mise(s) à jour de pilotes disponible(s).";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Erreur Windows Update : {ex.Message}";
        }
        finally { IsSearchingUpdates = false; }
    }

    private static List<DriverUpdateInfo> FindDriverUpdatesViaWuApi()
    {
        var results = new List<DriverUpdateInfo>();
        try
        {
            // WUApiLib est un composant COM toujours présent sur Windows.
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType == null) return results;

            dynamic session  = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online  = true;

            // Cherche les pilotes non installés et non masqués.
            dynamic result = searcher.Search("Type='Driver' AND IsInstalled=0 AND IsHidden=0");

            for (int i = 0; i < result.Updates.Count; i++)
            {
                dynamic update = result.Updates.Item(i);
                results.Add(new DriverUpdateInfo(
                    Title:       (string)update.Title,
                    SizeBytes:   (long)update.MaxDownloadSize,
                    IsDownloaded:(bool)update.IsDownloaded));
            }
        }
        catch { /* WUApi indisponible ou réseau absent */ }
        return results;
    }

    private async Task InstallSelectedUpdateAsync()
    {
        if (SelectedUpdate is null) return;

        var confirmed = _dialogs.Confirm(
            "Installer la mise à jour",
            $"Installer « {SelectedUpdate.Title} » ({SelectedUpdate.SizeDisplay}) via Windows Update ?\n\n" +
            "Windows Update gérera le téléchargement et l'installation.");
        if (!confirmed) return;

        IsSearchingUpdates = true;
        UpdateStatus = $"Installation de « {SelectedUpdate.Title} »…";
        try
        {
            await Task.Run(() => InstallViaWuApi(SelectedUpdate.Title));
            UpdateStatus = $"Installation lancée. Redémarrage éventuellement nécessaire.";
            DriverUpdates.Remove(SelectedUpdate);
            SelectedUpdate = null;
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Erreur d'installation : {ex.Message}";
        }
        finally { IsSearchingUpdates = false; }
    }

    private static void InstallViaWuApi(string updateTitle)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType == null) throw new InvalidOperationException("WUApi non disponible.");

        dynamic session  = Activator.CreateInstance(sessionType)!;
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online  = true;

        dynamic searchResult = searcher.Search("Type='Driver' AND IsInstalled=0 AND IsHidden=0");

        // Trouver la mise à jour correspondante.
        dynamic? targetUpdate = null;
        for (int i = 0; i < searchResult.Updates.Count; i++)
        {
            dynamic u = searchResult.Updates.Item(i);
            if (string.Equals((string)u.Title, updateTitle, StringComparison.OrdinalIgnoreCase))
            { targetUpdate = u; break; }
        }
        if (targetUpdate is null) throw new InvalidOperationException("Mise à jour introuvable dans Windows Update.");

        // Créer collection → downloader → installer.
        dynamic collection = Activator.CreateInstance(
            Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
        collection.Add(targetUpdate);

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = collection;
        downloader.Download();

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = collection;
        installer.Install();
    }

    private void OpenWindowsUpdate() =>
        TryOpen("ms-settings:windowsupdate-optionalupdates");

    private void SearchManufacturer()
    {
        if (Selected is null) return;
        TryOpen($"https://www.bing.com/search?q={Uri.EscapeDataString($"{Selected.Manufacturer} {Selected.DeviceName} driver")}");
    }

    private void TryOpen(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { _dialogs.Warn("Ouverture impossible", ex.Message); }
    }
}
