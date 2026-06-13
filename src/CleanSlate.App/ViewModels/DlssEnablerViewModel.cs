using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Tuile d'un jeu dans la bibliothèque visuelle : jaquette (locale ou CDN Steam),
/// badge « mod installé » et état de sélection.
/// </summary>
public sealed class GameTile : ObservableObject
{
    private bool _installed;
    private bool _isSelected;
    private string? _coverImage;

    public GameTile(InstalledGame game, bool installed)
    {
        Game = game;
        _installed = installed;
        _coverImage = game.CoverImage;
    }

    public InstalledGame Game { get; }

    public string Name => Game.Name;
    public string Source => Game.Source;
    public string InstallDir => Game.InstallDir;

    /// <summary>Jaquette : connue au scan, ou résolue ensuite par nom (jeux hors Steam).</summary>
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

    /// <summary>Jeu installé via le Xbox Game Pass : l'installation du mod peut être bloquée
    /// ou effacée par la vérification d'intégrité du package (badge d'avertissement).</summary>
    public bool IsGamePass => Game.Source == "Xbox Game Pass";

    /// <summary>Initiale affichée sur la tuile de remplacement quand il n'y a pas de jaquette.</summary>
    public string Initial => string.IsNullOrEmpty(Game.Name)
        ? "?"
        : Game.Name[..1].ToUpperInvariant();

    public bool Installed
    {
        get => _installed;
        set => SetProperty(ref _installed, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Sous-onglet « DLSS Enabler » du Mode Jeu : gestionnaire du mod DLSS Enabler
/// (github.com/artur-graniszewski/DLSS-Enabler, Nexus Mods site/mods/757), à la
/// manière de « DLSS Enabler Manager » — bibliothèque visuelle des jeux installés
/// (Steam / Epic / dossiers manuels mémorisés) avec jaquettes, détection du mod,
/// installation silencieuse et désinstallation propre.
/// </summary>
public sealed class DlssEnablerViewModel : ObservableObject
{
    private readonly IDlssEnablerService _service;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;

    private GameTile? _selectedTile;
    private string _status = "Détection des jeux en cours…";
    private string _gameStatusText = string.Empty;
    private string _compatibilityText = string.Empty;
    private string _filterText = string.Empty;
    private bool _isInstalled;
    private bool _isBusy;
    private bool _hasScanned;

    public DlssEnablerViewModel(IDlssEnablerService service, IAppSettingsService settings, IDialogService dialogs)
    {
        _service = service;
        _settings = settings;
        _dialogs = dialogs;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = o => o is GameTile t &&
            (string.IsNullOrWhiteSpace(_filterText) ||
             t.Name.Contains(_filterText.Trim(), StringComparison.OrdinalIgnoreCase));

        ScanGamesCommand = new AsyncRelayCommand(ScanGamesAsync, () => !IsBusy);
        PickFolderCommand = new RelayCommand(PickFolder, () => !IsBusy);
        SelectGameCommand = new RelayCommand<GameTile>(SelectGame);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && SelectedTile is not null && !IsInstalled);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => !IsBusy && SelectedTile is not null && IsInstalled);
        OpenProjectPageCommand = new RelayCommand(() => OpenUrl("https://github.com/artur-graniszewski/DLSS-Enabler"));
    }

    public ObservableCollection<GameTile> Games { get; } = new();

    /// <summary>Vue filtrée par <see cref="FilterText"/> pour la grille de jaquettes.</summary>
    public ICollectionView GamesView { get; }

    public AsyncRelayCommand ScanGamesCommand { get; }
    public RelayCommand PickFolderCommand { get; }
    public RelayCommand<GameTile> SelectGameCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public RelayCommand OpenProjectPageCommand { get; }

    public GameTile? SelectedTile
    {
        get => _selectedTile;
        private set
        {
            if (SetProperty(ref _selectedTile, value))
            {
                OnPropertyChanged(nameof(HasSelectedGame));
                RefreshStatus();
            }
        }
    }

    public bool HasSelectedGame => _selectedTile is not null;

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

    /// <summary>Affiche le texte d'invite « Rechercher un jeu… » quand le filtre est vide.</summary>
    public bool ShowFilterHint => string.IsNullOrEmpty(_filterText);

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>État du mod pour le jeu sélectionné (installé ou non + fichiers détectés).</summary>
    public string GameStatusText { get => _gameStatusText; private set => SetProperty(ref _gameStatusText, value); }

    /// <summary>Diagnostic de compatibilité DLSS du jeu sélectionné.</summary>
    public string CompatibilityText
    {
        get => _compatibilityText;
        private set
        {
            if (SetProperty(ref _compatibilityText, value))
                OnPropertyChanged(nameof(HasCompatibilityText));
        }
    }

    public bool HasCompatibilityText => !string.IsNullOrEmpty(_compatibilityText);

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
                RaiseCommandsCanExecute();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RaiseCommandsCanExecute();
        }
    }

    public string HonestNotice =>
        "DLSS Enabler est un mod open-source (artur-graniszewski) qui simule DLSS " +
        "Super Resolution et Frame Generation — y compris le Multi Frame Generation " +
        "(x2 à x6, façon DLSS 4.5) — sur n'importe quel GPU DirectX 12, dans les jeux " +
        "qui prennent en charge DLSS2/DLSS3 nativement. Le DLL OFFICIEL du mod est " +
        $"intégré à CleanSlate (v{_service.EmbeddedVersion}, aucun téléchargement) : il est " +
        "copié À CÔTÉ DE L'EXÉCUTABLE du jeu (localisé automatiquement, même dans les " +
        "sous-dossiers type bin\\x64) sous le nom de proxy le plus sûr (ou en " +
        "plugin ASI si nécessaire), avec désinstallation propre en un clic. ⚠️ N'utilisez " +
        "JAMAIS ce mod dans un jeu multijoueur protégé par un anticheat : l'injection de " +
        "DLL peut entraîner un bannissement. Réservez-le aux jeux solo.";

    /// <summary>
    /// Lance la première détection automatiquement (appelé au chargement de la vue) :
    /// la bibliothèque est déjà remplie quand l'utilisateur ouvre l'onglet.
    /// </summary>
    public Task AutoScanAsync()
    {
        if (_hasScanned || IsBusy) return Task.CompletedTask;
        return ScanGamesAsync();
    }

    private void RaiseCommandsCanExecute()
    {
        ScanGamesCommand.RaiseCanExecuteChanged();
        PickFolderCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        UninstallCommand.RaiseCanExecuteChanged();
    }

    private async Task ScanGamesAsync()
    {
        IsBusy = true;
        _hasScanned = true;
        Status = "Recherche des jeux installés (Steam, Epic Games)…";
        try
        {
            var detected = await _service.ScanGamesAsync(CancellationToken.None);

            // Dossiers ajoutés à la main : ceux de la session + ceux mémorisés.
            var manualDirs = Games.Where(t => t.Source == "Manuel").Select(t => t.InstallDir)
                .Concat(_settings.Load().ManualGameFolders)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .ToList();

            var all = detected.ToList();
            foreach (var dir in manualDirs)
            {
                if (!all.Any(g => string.Equals(g.InstallDir, dir, StringComparison.OrdinalIgnoreCase)))
                    all.Add(new InstalledGame(DirToName(dir), dir, "Manuel"));
            }

            // L'état du mod est vérifié en arrière-plan (accès disque par jeu).
            var tiles = await Task.Run(() =>
                all.Select(g => new GameTile(g, SafeInstalled(g.InstallDir))).ToList());

            var selectedDir = SelectedTile?.InstallDir;
            Games.Clear();
            foreach (var t in tiles) Games.Add(t);
            OnPropertyChanged(nameof(HasGames));

            // On re-sélectionne le même jeu si toujours présent après re-scan.
            var again = selectedDir is null ? null : Games.FirstOrDefault(t =>
                string.Equals(t.InstallDir, selectedDir, StringComparison.OrdinalIgnoreCase));
            if (again is not null) SelectGame(again);
            else SelectedTile = null;

            Status = Games.Count == 0
                ? "Aucun jeu détecté automatiquement. Utilisez « Ajouter un dossier… » pour pointer le dossier d'un jeu."
                : $"{Games.Count} jeu(x) trouvé(s). Cliquez sur un jeu pour gérer le mod.";
        }
        catch (Exception ex)
        {
            // Pas de boîte de dialogue : le scan peut être lancé automatiquement au
            // chargement de la vue, une erreur ne doit pas interrompre l'utilisateur.
            Status = $"Détection impossible : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // Jaquettes manquantes (Epic, Game Pass, dossiers manuels) : on tente de les
        // retrouver par nom via Steam, en tâche de fond, sans bloquer l'affichage.
        _ = ResolveMissingCoversAsync();
    }

    /// <summary>
    /// Complète les jaquettes absentes en interrogeant Steam par le nom du jeu. Best-effort
    /// (réseau optionnel) : chaque tuile est mise à jour dès qu'une image est trouvée.
    /// </summary>
    private async Task ResolveMissingCoversAsync()
    {
        var todo = Games.Where(t => !t.HasCover).ToList();
        foreach (var tile in todo)
        {
            try
            {
                var url = await _service.ResolveCoverUrlAsync(tile.Name, CancellationToken.None);
                if (!string.IsNullOrEmpty(url)) tile.CoverImage = url;
            }
            catch { /* best-effort : on garde la tuile d'origine */ }
        }
    }

    private static string DirToName(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? dir : name;
    }

    private bool SafeInstalled(string dir)
    {
        try { return _service.GetStatus(dir).Installed; }
        catch { return false; }
    }

    private void SelectGame(GameTile? tile)
    {
        if (tile is null) return;
        foreach (var t in Games) t.IsSelected = ReferenceEquals(t, tile);
        SelectedTile = tile;
    }

    private void PickFolder()
    {
        var dir = _dialogs.PickFolder("Choisissez le dossier d'installation du jeu (celui de l'exécutable)");
        if (dir is null) return;

        var existing = Games.FirstOrDefault(t =>
            string.Equals(t.InstallDir, dir, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new GameTile(new InstalledGame(DirToName(dir), dir, "Manuel"), SafeInstalled(dir));
            Games.Add(existing);
            OnPropertyChanged(nameof(HasGames));
            SaveManualFolders();
        }
        SelectGame(existing);
    }

    /// <summary>Mémorise les dossiers manuels pour les retrouver aux prochaines sessions.</summary>
    private void SaveManualFolders()
    {
        var dirs = Games.Where(t => t.Source == "Manuel").Select(t => t.InstallDir).ToArray();
        _settings.Save(_settings.Load() with { ManualGameFolders = dirs });
    }

    private void RefreshStatus()
    {
        if (_selectedTile is null)
        {
            GameStatusText = string.Empty;
            CompatibilityText = string.Empty;
            IsInstalled = false;
            return;
        }

        try
        {
            var status = _service.GetStatus(_selectedTile.InstallDir);
            IsInstalled = status.Installed;
            _selectedTile.Installed = status.Installed;
            GameStatusText = status.Installed
                ? $"✅ DLSS Enabler est installé dans ce jeu ({string.Join(", ", status.DetectedFiles)})."
                : "❌ DLSS Enabler n'est pas installé dans ce jeu.";

            var compat = _service.GetCompatibility(_selectedTile.InstallDir);
            CompatibilityText = compat.Summary;
        }
        catch (Exception ex)
        {
            IsInstalled = false;
            GameStatusText = $"État indéterminé : {ex.Message}";
            CompatibilityText = string.Empty;
        }
    }

    private async Task InstallAsync()
    {
        if (_selectedTile is null) return;
        var game = _selectedTile.Game;

        var gamePassWarning = _selectedTile.IsGamePass
            ? "\n\n⚠️ Ce jeu vient du Xbox Game Pass : son dossier est protégé par Windows. " +
              "CleanSlate localise l'exécutable et tente le déverrouillage automatiquement " +
              "(des instructions s'affichent si besoin). À savoir : une mise à jour ou une " +
              "réparation du jeu peut supprimer les DLL ajoutées — sans risque pour le jeu, " +
              "il suffira de réinstaller le mod."
            : string.Empty;

        // Avertissement si le jeu n'a pas l'air de supporter le DLSS : inutile d'installer.
        var compat = _service.GetCompatibility(game.InstallDir);
        var compatWarning = compat.Level == DlssCompatibility.Unlikely
            ? "\n\n⛔ ATTENTION : aucun composant DLSS/FSR/XeSS n'a été trouvé dans ce jeu. " +
              "DLSS Enabler n'AJOUTE PAS le DLSS à un jeu qui n'en a pas — il ne fait que " +
              "l'activer là où il existe déjà. L'installer ici n'aura très probablement AUCUN effet."
            : compat.Level == DlssCompatibility.Maybe
                ? "\n\n🟡 Pas de DLSS natif détecté, mais un upscaler FSR/XeSS est présent : " +
                  "le mod peut aider (Frame Generation), sans garantie."
                : string.Empty;

        var confirmed = _dialogs.Confirm("Installer DLSS Enabler",
            $"Installer le mod DLSS Enabler dans :\n{game.InstallDir}\n\n" +
            "⚠️ Rappel : uniquement pour les jeux SOLO. Dans un jeu multijoueur protégé " +
            "par un anticheat, ce mod peut entraîner un bannissement." + compatWarning + gamePassWarning +
            "\n\nContinuer ?");
        if (!confirmed) return;

        IsBusy = true;
        Status = "Installation du mod (localisation de l'exécutable du jeu)…";
        try
        {
            var result = await _service.InstallAsync(game.InstallDir, CancellationToken.None);

            RefreshStatus();
            if (result.Success)
            {
                Status = $"DLSS Enabler v{_service.EmbeddedVersion} installé dans « {game.Name} » " +
                         $"({result.InstalledFile}).";
                _dialogs.Info("DLSS Enabler",
                    $"DLSS Enabler v{_service.EmbeddedVersion} est installé dans « {game.Name} ».\n\n" +
                    $"Fichier posé : {result.InstalledFile}\nÀ côté de l'exécutable, dans :\n{result.TargetDir}\n\n" +
                    "Lancez le jeu, puis ouvrez l'overlay du mod (Fin / End par défaut) pour activer le " +
                    "DLSS Super Resolution et la Frame Generation. Le multiplicateur de Multi Frame " +
                    "Generation (x2 à x6) se choisit dans cet overlay — x5 et x6 (DLSS 4.5) nécessitent " +
                    "des fichiers Streamline récents (2.11+) dans le jeu et un framerate de base d'au " +
                    "moins 40-50 FPS pour rester fluide. Activez aussi le DLSS dans les options " +
                    "graphiques du jeu.");
            }
            else if (result.Failure == DlssInstallFailure.WriteDenied && _selectedTile.IsGamePass)
            {
                Status = string.Empty;
                _dialogs.Warn("DLSS Enabler — jeu Xbox Game Pass",
                    "Windows refuse l'écriture dans le dossier de ce jeu Game Pass : c'est la " +
                    "protection normale des jeux installés par l'app Xbox.\n\n" +
                    "Pour déverrouiller le dossier, deux méthodes :\n" +
                    "1. Dans l'app Xbox : page du jeu → bouton « ⋯ » → Gérer → Fichiers → " +
                    "« Activer les fonctionnalités de modding avancées » (selon le jeu), puis réessayez ;\n" +
                    "2. Ou relancez CleanSlate en tant qu'administrateur (bouton en haut de la " +
                    "fenêtre) : CleanSlate déverrouillera le dossier automatiquement.\n\n" +
                    "Dossier concerné :\n" + result.TargetDir);
            }
            else if (result.Failure == DlssInstallFailure.WriteDenied)
            {
                Status = string.Empty;
                _dialogs.Warn("DLSS Enabler",
                    "Windows refuse l'écriture dans le dossier du jeu :\n" + result.TargetDir +
                    "\n\nRelancez CleanSlate en tant qu'administrateur (bouton en haut de la " +
                    "fenêtre) puis réessayez.");
            }
            else
            {
                Status = string.Empty;
                _dialogs.Warn("DLSS Enabler",
                    "L'installation n'a pas pu copier les fichiers du mod dans ce dossier. " +
                    "Vérifiez que le jeu n'est pas en cours d'exécution et que vous avez les " +
                    "droits d'écriture sur son dossier.");
            }
        }
        catch (Exception ex)
        {
            Status = string.Empty;
            _dialogs.Warn("DLSS Enabler", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallAsync()
    {
        if (_selectedTile is null) return;
        var game = _selectedTile.Game;

        var confirmed = _dialogs.Confirm("Désinstaller DLSS Enabler",
            $"Retirer le mod DLSS Enabler de :\n{game.InstallDir} ?\n\n" +
            "Les fichiers du jeu ne sont pas touchés ; seuls ceux du mod sont retirés " +
            "(les DLL d'autres mods, comme ReShade, sont préservées).");
        if (!confirmed) return;

        IsBusy = true;
        Status = "Désinstallation…";
        try
        {
            var ok = await _service.UninstallAsync(game.InstallDir, CancellationToken.None);
            RefreshStatus();
            Status = ok
                ? $"DLSS Enabler retiré de « {game.Name} »."
                : "La désinstallation n'a pas pu retirer tous les fichiers du mod.";
        }
        catch (Exception ex)
        {
            Status = string.Empty;
            _dialogs.Warn("DLSS Enabler", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _dialogs.Warn("Navigateur", ex.Message); }
    }
}
