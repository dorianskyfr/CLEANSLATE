using System.Collections.ObjectModel;
using System.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Sous-onglet « DLSS Enabler » du Mode Jeu : gestionnaire du mod DLSS Enabler
/// (github.com/artur-graniszewski/DLSS-Enabler, Nexus Mods site/mods/757), à la
/// manière de « DLSS Enabler Manager » — scan des jeux installés (Steam / Epic /
/// dossier manuel), détection du mod, installation silencieuse et désinstallation.
/// </summary>
public sealed class DlssEnablerViewModel : ObservableObject
{
    private readonly IDlssEnablerService _service;
    private readonly IDialogService _dialogs;

    private InstalledGame? _selectedGame;
    private string _status = "Cliquez sur « Détecter mes jeux » pour commencer.";
    private string _gameStatusText = string.Empty;
    private bool _isInstalled;
    private bool _isBusy;

    public DlssEnablerViewModel(IDlssEnablerService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;

        ScanGamesCommand = new AsyncRelayCommand(ScanGamesAsync, () => !IsBusy);
        PickFolderCommand = new RelayCommand(PickFolder, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && SelectedGame is not null && !IsInstalled);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => !IsBusy && SelectedGame is not null && IsInstalled);
        OpenProjectPageCommand = new RelayCommand(() => OpenUrl("https://github.com/artur-graniszewski/DLSS-Enabler"));
    }

    public ObservableCollection<InstalledGame> Games { get; } = new();

    public AsyncRelayCommand ScanGamesCommand { get; }
    public RelayCommand PickFolderCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public RelayCommand OpenProjectPageCommand { get; }

    public InstalledGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnPropertyChanged(nameof(HasSelectedGame));
                RefreshStatus();
            }
        }
    }

    public bool HasSelectedGame => _selectedGame is not null;

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>État du mod pour le jeu sélectionné (installé ou non + fichiers détectés).</summary>
    public string GameStatusText { get => _gameStatusText; private set => SetProperty(ref _gameStatusText, value); }

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
        "Super Resolution et Frame Generation sur n'importe quel GPU DirectX 12, dans " +
        "les jeux qui prennent en charge DLSS2/DLSS3 nativement. CleanSlate télécharge " +
        "l'installateur OFFICIEL depuis GitHub et le pose dans le dossier du jeu choisi " +
        "(quelques DLL), avec désinstallation propre en un clic. ⚠️ N'utilisez JAMAIS " +
        "ce mod dans un jeu multijoueur protégé par un anticheat : l'injection de DLL " +
        "peut entraîner un bannissement. Réservez-le aux jeux solo.";

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
        Status = "Recherche des jeux installés (Steam, Epic Games)…";
        try
        {
            var games = await _service.ScanGamesAsync(CancellationToken.None);

            // Conserver les dossiers ajoutés à la main lors du re-scan.
            var manual = Games.Where(g => g.Source == "Manuel").ToList();
            Games.Clear();
            foreach (var g in games) Games.Add(g);
            foreach (var m in manual)
                if (!Games.Any(g => string.Equals(g.InstallDir, m.InstallDir, StringComparison.OrdinalIgnoreCase)))
                    Games.Add(m);

            Status = Games.Count == 0
                ? "Aucun jeu détecté automatiquement. Utilisez « Choisir un dossier… » pour pointer le dossier d'un jeu."
                : $"{Games.Count} jeu(x) trouvé(s). Sélectionnez un jeu pour voir l'état du mod.";
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

    private void PickFolder()
    {
        var dir = _dialogs.PickFolder("Choisissez le dossier d'installation du jeu (celui de l'exécutable)");
        if (dir is null) return;

        var game = new InstalledGame(Path.GetFileName(dir.TrimEnd('\\', '/')), dir, "Manuel");
        var existing = Games.FirstOrDefault(g =>
            string.Equals(g.InstallDir, game.InstallDir, StringComparison.OrdinalIgnoreCase));
        if (existing is null) Games.Add(game);
        SelectedGame = existing ?? game;
    }

    private void RefreshStatus()
    {
        if (_selectedGame is null)
        {
            GameStatusText = string.Empty;
            IsInstalled = false;
            return;
        }

        try
        {
            var status = _service.GetStatus(_selectedGame.InstallDir);
            IsInstalled = status.Installed;
            GameStatusText = status.Installed
                ? $"✅ DLSS Enabler est installé dans ce jeu ({string.Join(", ", status.DetectedFiles)})."
                : "❌ DLSS Enabler n'est pas installé dans ce jeu.";
        }
        catch (Exception ex)
        {
            IsInstalled = false;
            GameStatusText = $"État indéterminé : {ex.Message}";
        }
    }

    private async Task InstallAsync()
    {
        if (_selectedGame is null) return;

        var confirmed = _dialogs.Confirm("Installer DLSS Enabler",
            $"Installer le mod DLSS Enabler dans :\n{_selectedGame.InstallDir}\n\n" +
            "⚠️ Rappel : uniquement pour les jeux SOLO. Dans un jeu multijoueur protégé " +
            "par un anticheat, ce mod peut entraîner un bannissement.\n\nContinuer ?");
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            Status = "Recherche de la dernière version sur GitHub…";
            var release = await _service.GetLatestReleaseAsync(CancellationToken.None);
            if (release is null)
            {
                Status = string.Empty;
                _dialogs.Warn("DLSS Enabler", "Impossible de récupérer la dernière version sur GitHub.");
                return;
            }

            Status = $"Téléchargement de {release.InstallerName}…";
            var progress = new Progress<double>(p => Status = $"Téléchargement : {p:0}%…");
            var installer = await _service.DownloadInstallerAsync(release, progress, CancellationToken.None);

            Status = "Installation silencieuse dans le dossier du jeu…";
            var ok = await _service.InstallAsync(installer, _selectedGame.InstallDir, CancellationToken.None);

            RefreshStatus();
            if (ok)
            {
                Status = $"DLSS Enabler v{release.Version} installé dans « {_selectedGame.Name} ».";
                _dialogs.Info("DLSS Enabler",
                    $"DLSS Enabler v{release.Version} est installé.\n\n" +
                    "Lancez le jeu puis activez DLSS (Super Resolution / Frame Generation) " +
                    "dans ses options graphiques. Si le jeu est très moddé, consultez la page " +
                    "du projet pour les variantes d'installation.");
            }
            else
            {
                Status = string.Empty;
                _dialogs.Warn("DLSS Enabler",
                    "L'installation ne s'est pas terminée correctement. " +
                    "Réessayez, ou installez manuellement depuis la page du projet.");
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
        if (_selectedGame is null) return;

        var confirmed = _dialogs.Confirm("Désinstaller DLSS Enabler",
            $"Retirer le mod DLSS Enabler de :\n{_selectedGame.InstallDir} ?\n\n" +
            "Les fichiers du jeu ne sont pas touchés ; seuls ceux du mod sont retirés " +
            "(les DLL d'autres mods, comme ReShade, sont préservées).");
        if (!confirmed) return;

        IsBusy = true;
        Status = "Désinstallation…";
        try
        {
            var ok = await _service.UninstallAsync(_selectedGame.InstallDir, CancellationToken.None);
            RefreshStatus();
            Status = ok
                ? $"DLSS Enabler retiré de « {_selectedGame.Name} »."
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
