using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Origine d'une tuile de bibliothèque (détermine l'action du bouton principal).</summary>
public enum GameTileKind { Installed, Catalog, OpenSource, Download }

/// <summary>
/// Tuile de jeu dans la bibliothèque. Origines possibles :
///  • un jeu INSTALLÉ (Steam/Epic/Game Pass) → bouton « ▶ Lancer » ;
///  • un jeu du CATALOGUE mondial Steam → bouton « 🛒 Voir sur Steam » (page officielle) ;
///  • un jeu OPEN-SOURCE → bouton « ⬇️ Télécharger » (binaire officiel HTTPS) ;
///  • une ressource d'une SOURCE JSON utilisateur → bouton « ⬇️ Télécharger » (URL directe HTTPS).
/// </summary>
public sealed class GameLibraryTile : ObservableObject
{
    private string? _coverImage;

    private GameLibraryTile(string name, string? cover)
    {
        Name        = name;
        _coverImage = cover;
    }

    /// <summary>Tuile pour un jeu installé localement (lançable).</summary>
    public static GameLibraryTile ForInstalled(InstalledGame game) =>
        new(game.Name, game.CoverImage)
        {
            Kind          = GameTileKind.Installed,
            InstalledGame = game,
            SourceBadge   = game.Source switch
            {
                "Steam"          => "Steam",
                "Epic Games"     => "Epic",
                "Xbox Game Pass" => "Game Pass",
                _                => game.Source,
            },
        };

    /// <summary>Tuile pour un jeu du catalogue Steam (non installé) : ouvre la page du magasin.</summary>
    public static GameLibraryTile ForCatalog(CatalogGame game) =>
        new(game.Name, game.CoverUrl)
        {
            Kind        = GameTileKind.Catalog,
            CatalogGame = game,
            SourceBadge = "Steam",
        };

    /// <summary>Tuile pour un jeu open-source téléchargeable depuis son dépôt officiel.</summary>
    public static GameLibraryTile ForOpenSource(OpenSourceGame game) =>
        new(game.Name, null)
        {
            Kind        = GameTileKind.OpenSource,
            SourceBadge = "Open-source",
            DownloadUrl = game.DownloadUrl,
            OfficialUrl = game.OfficialUrl,
        };

    /// <summary>Tuile pour une ressource d'une source JSON utilisateur (URL directe HTTPS).</summary>
    public static GameLibraryTile ForDownload(DownloadResource res) =>
        new(res.Name, null)
        {
            Kind        = GameTileKind.Download,
            SourceBadge = string.IsNullOrWhiteSpace(res.Category) ? "Téléchargement" : res.Category!,
            DownloadUrl = res.Url,
            Sha256      = res.Sha256,
        };

    public GameTileKind   Kind           { get; private init; }
    public InstalledGame? InstalledGame  { get; private init; }
    public CatalogGame?   CatalogGame    { get; private init; }

    public string Name        { get; }
    public string SourceBadge { get; private init; } = "Steam";

    public bool   IsInstalled => Kind == GameTileKind.Installed;
    public string InstallDir  => InstalledGame?.InstallDir ?? string.Empty;
    public string? StoreUrl   => CatalogGame?.StoreUrl;

    /// <summary>URL de téléchargement direct HTTPS (jeux open-source et ressources de sources JSON).</summary>
    public string? DownloadUrl { get; private init; }
    /// <summary>Somme de contrôle SHA-256 attendue (optionnelle), pour vérifier l'intégrité.</summary>
    public string? Sha256      { get; private init; }
    /// <summary>Page officielle du projet (repli si le téléchargement échoue).</summary>
    public string? OfficialUrl { get; private init; }

    public bool IsDownloadable => Kind is GameTileKind.OpenSource or GameTileKind.Download;

    /// <summary>Libellé du bouton d'action principal selon l'origine de la tuile.</summary>
    public string ActionLabel => Kind switch
    {
        GameTileKind.Installed => "▶ Lancer",
        GameTileKind.Catalog   => "🛒 Voir sur Steam",
        _                      => "⬇️ Télécharger",
    };

    public bool IsGamePass => InstalledGame?.Source == "Xbox Game Pass";

    public string? CoverImage
    {
        get => _coverImage;
        set
        {
            if (SetProperty(ref _coverImage, value))
                OnPropertyChanged(nameof(HasCover));
        }
    }

    public bool HasCover => !string.IsNullOrEmpty(_coverImage);

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}

/// <summary>
/// Bibliothèque de jeux « façon Hydra » mais légale :
///  • « Mes jeux » : les jeux installés (Steam, Epic, Xbox Game Pass) sont détectés via la
///    même infrastructure de scan que le DLSS Enabler et peuvent être lancés directement ;
///  • Recherche mondiale : en tapant dans la barre, on interroge le catalogue officiel
///    Steam (tous les jeux du monde) avec jaquettes, et on ouvre la page officielle du
///    magasin pour acheter/télécharger légalement.
///
/// CleanSlate ne télécharge aucun jeu et n'importe aucune « source » de repacks/torrents :
/// il n'aide pas à pirater des jeux.
/// </summary>
public sealed class GameLibraryViewModel : ObservableObject
{
    private const int MaxCatalogResults = 60;

    private readonly IDlssEnablerService _dlssService;
    private readonly IGameCatalogService _catalog;
    private readonly IFileDownloadService _downloader;
    private readonly IDialogService      _dialogs;

    private readonly List<GameLibraryTile> _installed = new();
    private CancellationTokenSource?       _searchCts;

    private string _filterText = string.Empty;
    private string _status      = "Cliquez sur « Scanner mes jeux », recherchez parmi tous les jeux du monde, ou découvrez les jeux open-source.";
    private bool   _isBusy;

    public GameLibraryViewModel(
        IDlssEnablerService dlssService,
        IGameCatalogService catalog,
        IFileDownloadService downloader,
        IDialogService dialogs)
    {
        _dlssService = dlssService;
        _catalog     = catalog;
        _downloader  = downloader;
        _dialogs     = dialogs;

        ScanCommand           = new AsyncRelayCommand(ScanAsync);
        ShowOpenSourceCommand = new RelayCommand(ShowOpenSource);
        ImportSourceCommand   = new RelayCommand(ImportSource);
        PrimaryActionCommand  = new RelayCommand<GameLibraryTile>(PrimaryAction);
    }

    public AsyncRelayCommand              ScanCommand           { get; }
    public RelayCommand                   ShowOpenSourceCommand { get; }
    public RelayCommand                   ImportSourceCommand   { get; }
    public RelayCommand<GameLibraryTile>  PrimaryActionCommand  { get; }

    /// <summary>Tuiles affichées (jeux installés, ou résultats de recherche du catalogue).</summary>
    public ObservableCollection<GameLibraryTile> Tiles { get; } = new();

    public bool HasTiles => Tiles.Count > 0;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            OnPropertyChanged(nameof(ShowFilterHint));
            _ = OnSearchChangedAsync(value);
        }
    }

    public bool ShowFilterHint => string.IsNullOrEmpty(_filterText);

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    // ------------------------------------------------------------------ Scan ----

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Détection des jeux installés en cours…";

        try
        {
            var found = await _dlssService.ScanGamesAsync(CancellationToken.None);

            _installed.Clear();
            foreach (var game in found)
                _installed.Add(GameLibraryTile.ForInstalled(game));

            if (string.IsNullOrWhiteSpace(_filterText))
                ShowInstalled();

            _ = ResolveMissingCoversAsync(found);
        }
        catch (Exception ex)
        {
            Status = $"Erreur lors du scan : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowInstalled()
    {
        Replace(_installed);
        Status = _installed.Count == 0
            ? "Aucun jeu installé détecté. Recherchez parmi tous les jeux du monde dans la barre ci-dessus."
            : $"{_installed.Count} jeu(x) installé(s) — cliquez sur ▶ pour lancer, ou recherchez d'autres jeux.";
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
                var tile = _installed.FirstOrDefault(t => t.InstallDir == game.InstallDir);
                if (tile != null) tile.CoverImage = url;
            }
            catch { }
        }
    }

    // -------------------------------------------------------- Recherche mondiale ----

    private async Task OnSearchChangedAsync(string query)
    {
        _searchCts?.Cancel();

        if (string.IsNullOrWhiteSpace(query))
        {
            ShowInstalled();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;

        try
        {
            // Anti-rebond : on attend que l'utilisateur arrête de taper.
            await Task.Delay(350, cts.Token);

            Status = $"Recherche de « {query} » parmi tous les jeux du monde…";

            var results = await _catalog.SearchAsync(query, MaxCatalogResults, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            // Les jeux installés correspondants passent en tête (lançables), sans doublon de nom.
            var installedMatches = _installed
                .Where(t => t.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            var installedNames = installedMatches
                .Select(t => t.Name)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

            var tiles = new List<GameLibraryTile>(installedMatches);
            tiles.AddRange(results
                .Where(g => !installedNames.Contains(g.Name))
                .Select(GameLibraryTile.ForCatalog));

            Replace(tiles);

            Status = tiles.Count == 0
                ? $"Aucun jeu trouvé pour « {query} »."
                : $"{tiles.Count} résultat(s) pour « {query} » — ▶ pour lancer un jeu installé, 🛒 pour ouvrir sa page Steam.";
        }
        catch (OperationCanceledException) { /* frappe suivante : ignoré */ }
        catch (Exception ex)
        {
            Status = $"Recherche indisponible : {ex.Message}";
        }
    }

    // ----------------------------------------------------- Jeux open-source ----

    private void ShowOpenSource()
    {
        _searchCts?.Cancel();
        var tiles = OpenSourceGameCatalog.Games.Select(GameLibraryTile.ForOpenSource).ToList();
        Replace(tiles);
        Status = $"{tiles.Count} jeux open-source — ⬇️ pour télécharger le binaire officiel et l'installer.";
    }

    // ---------------------------------------------- Source JSON utilisateur ----

    /// <summary>
    /// Importe un fichier « source » JSON listant des ressources à télécharger en HTTPS
    /// direct, et affiche celles-ci dans la grille. Les liens magnet/torrent éventuels
    /// sont volontairement ignorés par l'analyseur : CleanSlate ne télécharge que des
    /// fichiers via des URLs http(s) directes.
    /// </summary>
    private void ImportSource()
    {
        _searchCts?.Cancel();

        var file = _dialogs.PickFile("Importer une source de téléchargements (JSON)",
            "Fichiers JSON|*.json|Tous les fichiers|*.*");
        if (string.IsNullOrEmpty(file)) return;

        IReadOnlyList<DownloadResource> resources;
        try
        {
            resources = DownloadSourceParser.Parse(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Source invalide", $"Impossible de lire « {Path.GetFileName(file)} ».\n{ex.Message}");
            return;
        }

        if (resources.Count == 0)
        {
            Status = "Aucune ressource HTTPS directe trouvée dans cette source " +
                     "(les liens magnet/torrent sont ignorés).";
            Replace(Array.Empty<GameLibraryTile>());
            return;
        }

        Replace(resources.Select(GameLibraryTile.ForDownload).ToList());
        Status = $"{resources.Count} ressource(s) importée(s) depuis « {Path.GetFileName(file)} » — ⬇️ pour télécharger.";
    }

    // --------------------------------------------------------------- Actions ----

    private void PrimaryAction(GameLibraryTile? tile)
    {
        if (tile is null) return;
        switch (tile.Kind)
        {
            case GameTileKind.Installed: LaunchGame(tile); break;
            case GameTileKind.Catalog:   OpenStore(tile);  break;
            default:                     _ = DownloadAsync(tile); break; // OpenSource + Download
        }
    }

    private async Task DownloadAsync(GameLibraryTile tile)
    {
        if (string.IsNullOrEmpty(tile.DownloadUrl) || IsBusy) return;

        IsBusy = true;
        Status = $"⬇️ Téléchargement de « {tile.Name} »…";
        try
        {
            var progress = new Progress<double>(pct =>
                Status = $"⬇️ Téléchargement de « {tile.Name} »… {pct:0} %");

            var suggested = SanitizeName(tile.Name) + ".exe";
            var path = await _downloader.DownloadAsync(tile.DownloadUrl!, suggested, progress, CancellationToken.None);

            // Vérification d'intégrité si une somme SHA-256 est fournie par la source.
            if (!string.IsNullOrWhiteSpace(tile.Sha256))
            {
                Status = $"🔒 Vérification de l'intégrité de « {tile.Name} »…";
                var actual = await ComputeSha256Async(path);
                if (!string.Equals(actual, tile.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { }
                    Status = $"❌ Somme de contrôle incorrecte pour « {tile.Name} » — fichier supprimé.";
                    _dialogs.Warn("Intégrité non vérifiée",
                        $"La somme SHA-256 du fichier téléchargé ne correspond pas à celle annoncée " +
                        $"par la source. Le fichier a été supprimé par précaution.\n\n" +
                        $"Attendu : {tile.Sha256}\nObtenu  : {actual}");
                    return;
                }
            }

            Status = $"✅ « {tile.Name} » téléchargé. Ouverture du fichier…";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = $"Échec du téléchargement de « {tile.Name} ».";
            var fallback = string.IsNullOrEmpty(tile.OfficialUrl)
                ? string.Empty
                : $"\n\nVous pouvez le récupérer directement ici :\n{tile.OfficialUrl}";
            _dialogs.Warn("Téléchargement impossible",
                $"Impossible de télécharger « {tile.Name} ».\n{ex.Message}{fallback}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private void LaunchGame(GameLibraryTile tile)
    {
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

    private void OpenStore(GameLibraryTile tile)
    {
        if (string.IsNullOrEmpty(tile.StoreUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(tile.StoreUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Ouverture impossible", ex.Message);
        }
    }

    /// <summary>Remplace le contenu affiché et notifie l'état de la grille.</summary>
    private void Replace(IReadOnlyList<GameLibraryTile> tiles)
    {
        Tiles.Clear();
        foreach (var t in tiles) Tiles.Add(t);
        OnPropertyChanged(nameof(HasTiles));
    }
}
