using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Tuile de jeu dans le Hydra Launcher : jaquette, source, bouton de lancement.</summary>
public sealed class GameLibraryTile : ObservableObject
{
    private string? _coverImage;

    public GameLibraryTile(InstalledGame game)
    {
        Game       = game;
        _coverImage = game.CoverImage;
    }

    public InstalledGame Game       { get; }
    public string        Name       => Game.Name;
    public string        Source     => Game.Source;
    public string        InstallDir => Game.InstallDir;

    public string? CoverImage
    {
        get => _coverImage;
        set
        {
            if (SetProperty(ref _coverImage, value))
                OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover   => !string.IsNullOrEmpty(_coverImage);
    public bool IsGamePass => Game.Source == "Xbox Game Pass";

    public string Initial => string.IsNullOrEmpty(Game.Name) ? "?" : Game.Name[..1].ToUpperInvariant();

    public string SourceBadge => Game.Source switch
    {
        "Steam"          => "Steam",
        "Epic Games"     => "Epic",
        "Xbox Game Pass" => "Game Pass",
        _                => Game.Source,
    };
}

/// <summary>
/// Hydra Launcher : bibliothèque de jeux installés avec lancement direct.
/// Réutilise la même infrastructure de scan que le DLSS Enabler (ScanGamesAsync,
/// ResolveCoverUrlAsync, FindMainExecutable) — aucune donnée supplémentaire.
/// </summary>
public sealed class GameLibraryViewModel : ObservableObject
{
    private readonly IDlssEnablerService _dlssService;
    private readonly IDialogService      _dialogs;
    private string _filterText = string.Empty;
    private string _status     = "Cliquez sur « Scanner mes jeux » pour afficher votre bibliothèque.";
    private bool   _isScanning;

    public GameLibraryViewModel(IDlssEnablerService dlssService, IDialogService dialogs)
    {
        _dlssService = dlssService;
        _dialogs     = dialogs;

        ScanCommand   = new AsyncRelayCommand(ScanAsync);
        LaunchCommand = new RelayCommand<GameLibraryTile>(LaunchGame);

        GamesView        = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
    }

    public AsyncRelayCommand            ScanCommand   { get; }
    public RelayCommand<GameLibraryTile> LaunchCommand { get; }

    public ObservableCollection<GameLibraryTile> Games { get; } = new();
    public ICollectionView GamesView { get; }

    public bool HasGames => Games.Count > 0;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                OnPropertyChanged(nameof(ShowFilterHint));
                GamesView.Refresh();
            }
        }
    }

    public bool ShowFilterHint => string.IsNullOrEmpty(_filterText);

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    private bool FilterGame(object obj) =>
        obj is GameLibraryTile t &&
        (string.IsNullOrEmpty(_filterText) ||
         t.Name.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase));

    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        Status = "Détection des jeux en cours…";
        Games.Clear();
        OnPropertyChanged(nameof(HasGames));

        try
        {
            var found = await _dlssService.ScanGamesAsync(CancellationToken.None);
            foreach (var game in found)
                Games.Add(new GameLibraryTile(game));

            OnPropertyChanged(nameof(HasGames));
            Status = Games.Count == 0
                ? "Aucun jeu détecté (Steam / Epic / Xbox Game Pass)."
                : $"{Games.Count} jeu(x) détecté(s) — double-cliquez ou cliquez sur ▶ pour lancer.";

            _ = ResolveMissingCoversAsync(found);
        }
        catch (Exception ex)
        {
            Status = $"Erreur lors du scan : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ResolveMissingCoversAsync(IReadOnlyList<InstalledGame> games)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var game in games)
        {
            if (!string.IsNullOrEmpty(game.CoverImage)) continue;
            if (cts.Token.IsCancellationRequested) break;
            try
            {
                var url = await _dlssService.ResolveCoverUrlAsync(game.Name, cts.Token);
                if (string.IsNullOrEmpty(url)) continue;
                var tile = Games.FirstOrDefault(t => t.InstallDir == game.InstallDir);
                if (tile != null) tile.CoverImage = url;
            }
            catch { }
        }
    }

    private void LaunchGame(GameLibraryTile? tile)
    {
        if (tile == null) return;

        var exe = _dlssService.FindMainExecutable(tile.InstallDir);
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            _dialogs.Warn("Lancement impossible",
                $"L'exécutable principal de « {tile.Name} » est introuvable.\n" +
                $"Dossier : {tile.InstallDir}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Lancement impossible", ex.Message);
        }
    }
}
