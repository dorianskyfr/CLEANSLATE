using System.Collections.ObjectModel;
using System.Diagnostics;
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

/// <summary>
/// Module 2 — Mise à jour des pilotes.
///
/// On ne LISTE plus les pilotes installés (peu utile pour l'utilisateur) : le module
/// se concentre sur l'essentiel — chercher les mises à jour de pilotes via Windows
/// Update (WUApi) et les installer en un clic. Windows Update fournit des pilotes
/// signés et certifiés WHQL.
/// </summary>
public sealed class DriversViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    private bool   _isBusy;
    private bool   _hasSearched;
    private string _updateStatus = "Cliquez sur « Rechercher les mises à jour » pour interroger Windows Update.";

    public DriversViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        SearchDriverUpdatesCommand = new AsyncRelayCommand(SearchDriverUpdatesAsync, () => !IsBusy);
        InstallSelectedCommand     = new AsyncRelayCommand(InstallSelectedAsync,     () => SelectedUpdate is not null && !IsBusy);
        InstallAllCommand          = new AsyncRelayCommand(InstallAllAsync,          () => DriverUpdates.Count > 0 && !IsBusy);
        OpenWindowsUpdateCommand   = new RelayCommand(OpenWindowsUpdate);
    }

    public ObservableCollection<DriverUpdateInfo> DriverUpdates { get; } = new();

    public AsyncRelayCommand SearchDriverUpdatesCommand { get; }
    public AsyncRelayCommand InstallSelectedCommand     { get; }
    public AsyncRelayCommand InstallAllCommand          { get; }
    public RelayCommand      OpenWindowsUpdateCommand   { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RaiseAll(); }
    }

    /// <summary>Vrai dès qu'une recherche a été lancée (pilote l'affichage de la liste/état vide).</summary>
    public bool HasSearched
    {
        get => _hasSearched;
        private set { if (SetProperty(ref _hasSearched, value)) OnPropertyChanged(nameof(ShowUpToDate)); }
    }

    /// <summary>Affiche le message « tous à jour » uniquement après une recherche sans résultat.</summary>
    public bool ShowUpToDate => HasSearched && !IsBusy && DriverUpdates.Count == 0;

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    private DriverUpdateInfo? _selectedUpdate;
    public DriverUpdateInfo? SelectedUpdate
    {
        get => _selectedUpdate;
        set { if (SetProperty(ref _selectedUpdate, value)) InstallSelectedCommand.RaiseCanExecuteChanged(); }
    }

    private void RaiseAll()
    {
        SearchDriverUpdatesCommand.RaiseCanExecuteChanged();
        InstallSelectedCommand.RaiseCanExecuteChanged();
        InstallAllCommand.RaiseCanExecuteChanged();
    }

    // ---- Recherche WUApi ----

    private async Task SearchDriverUpdatesAsync()
    {
        IsBusy = true;
        UpdateStatus = "Interrogation de Windows Update (peut prendre 1-2 minutes)…";
        DriverUpdates.Clear();
        OnPropertyChanged(nameof(ShowUpToDate));

        try
        {
            var updates = await Task.Run(() => FindDriverUpdatesViaWuApi());
            foreach (var u in updates) DriverUpdates.Add(u);
            HasSearched = true;
            UpdateStatus = updates.Count == 0
                ? "Tous vos pilotes sont à jour. ✅"
                : $"{updates.Count} mise(s) à jour de pilotes disponible(s).";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Erreur Windows Update : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowUpToDate));
        }
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

    private async Task InstallSelectedAsync()
    {
        if (SelectedUpdate is null) return;

        var confirmed = _dialogs.Confirm(
            "Installer la mise à jour",
            $"Installer « {SelectedUpdate.Title} » ({SelectedUpdate.SizeDisplay}) via Windows Update ?\n\n" +
            "Windows Update gérera le téléchargement et l'installation.");
        if (!confirmed) return;

        await InstallTitlesAsync(new[] { SelectedUpdate.Title });
    }

    private async Task InstallAllAsync()
    {
        if (DriverUpdates.Count == 0) return;

        var titles = DriverUpdates.Select(u => u.Title).ToArray();
        var confirmed = _dialogs.Confirm(
            "Installer toutes les mises à jour",
            $"Installer les {titles.Length} mise(s) à jour de pilotes via Windows Update ?\n\n" +
            "Le téléchargement et l'installation sont gérés par Windows. " +
            "Un redémarrage peut être nécessaire.");
        if (!confirmed) return;

        await InstallTitlesAsync(titles);
    }

    private async Task InstallTitlesAsync(IReadOnlyList<string> titles)
    {
        IsBusy = true;
        UpdateStatus = $"Installation de {titles.Count} pilote(s)…";
        try
        {
            await Task.Run(() => InstallViaWuApi(titles));
            UpdateStatus = "Installation lancée. Un redémarrage peut être nécessaire.";
            foreach (var t in titles.ToList())
            {
                var match = DriverUpdates.FirstOrDefault(u => u.Title == t);
                if (match is not null) DriverUpdates.Remove(match);
            }
            SelectedUpdate = null;
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Erreur d'installation : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowUpToDate));
        }
    }

    private static void InstallViaWuApi(IReadOnlyList<string> updateTitles)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType == null) throw new InvalidOperationException("WUApi non disponible.");

        dynamic session  = Activator.CreateInstance(sessionType)!;
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online  = true;

        dynamic searchResult = searcher.Search("Type='Driver' AND IsInstalled=0 AND IsHidden=0");

        dynamic collection = Activator.CreateInstance(
            Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;

        var wanted = updateTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < searchResult.Updates.Count; i++)
        {
            dynamic u = searchResult.Updates.Item(i);
            if (wanted.Contains((string)u.Title))
                collection.Add(u);
        }
        if (collection.Count == 0)
            throw new InvalidOperationException("Mise(s) à jour introuvable(s) dans Windows Update.");

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = collection;
        downloader.Download();

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = collection;
        installer.Install();
    }

    private void OpenWindowsUpdate()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); }
        catch (Exception ex) { _dialogs.Warn("Ouverture impossible", ex.Message); }
    }
}
