using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Une ligne de résultat de l'analyseur d'espace disque (dossier ou fichier).</summary>
public sealed class DiskUsageRowViewModel : ObservableObject
{
    public DiskUsageRowViewModel(DiskUsageEntry entry, long maxSize)
    {
        Entry       = entry;
        SizeDisplay = FileActionLogger.FormatBytes(entry.SizeBytes);
        BarPercent  = maxSize > 0 ? (double)entry.SizeBytes / maxSize * 100 : 0;
        Icon        = entry.IsDirectory ? "📁" : "📄";
    }

    public DiskUsageEntry Entry { get; }
    public string Name        => Entry.Name;
    public string Path        => Entry.Path;
    public string SizeDisplay { get; }
    public double BarPercent  { get; }
    public string Icon        { get; }
}

/// <summary>Un groupe de fichiers identiques dans l'onglet « Doublons ».</summary>
public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        Header = $"{group.Files.Count} copies × {FileActionLogger.FormatBytes(group.SizeBytes)} " +
                 $"— {FileActionLogger.FormatBytes(group.WastedBytes)} récupérables";
        Files = group.Files.Select(f => f.Path).ToList();
    }

    public string Header { get; }
    public IReadOnlyList<string> Files { get; }
}

/// <summary>
/// Analyseur d'espace disque (lecture seule) : choisissez un lecteur ou un dossier,
/// CleanSlate liste ses plus gros sous-dossiers/fichiers pour trouver ce qui remplit
/// le disque. Rien n'est jamais supprimé ici — un clic ouvre l'emplacement dans
/// l'Explorateur pour agir vous-même.
/// </summary>
public sealed class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskAnalyzer _analyzer;
    private readonly IDuplicateFinder _duplicateFinder;
    private readonly IDialogService _dialogs;

    private string _selectedPath;
    private string _status = "Choisissez un lecteur ou saisissez un dossier, puis lancez l'analyse.";
    private string _totalDisplay = "—";
    private bool _isBusy;

    private string _duplicateStatus = "Choisissez une taille minimale, puis cherchez les doublons.";
    private string _wastedDisplay = "—";
    private string _selectedMinSizeLabel = "1 Mo";

    public DiskAnalyzerViewModel(IDiskAnalyzer analyzer, IDuplicateFinder duplicateFinder, IDialogService dialogs)
    {
        _analyzer = analyzer;
        _duplicateFinder = duplicateFinder;
        _dialogs  = dialogs;

        Drives = new ObservableCollection<string>(GetFixedDrives());
        _selectedPath = Drives.FirstOrDefault() ?? @"C:\";

        AnalyzeCommand        = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy);
        FindDuplicatesCommand = new AsyncRelayCommand(FindDuplicatesAsync, () => !IsBusy);
        OpenCommand           = new RelayCommand<DiskUsageRowViewModel>(Open);
        OpenPathCommand       = new RelayCommand<string>(OpenPath);
    }

    public ObservableCollection<string> Drives { get; }
    public ObservableCollection<DiskUsageRowViewModel> Results { get; } = new();
    public ObservableCollection<DuplicateGroupViewModel> Duplicates { get; } = new();

    public string SelectedPath { get => _selectedPath; set => SetProperty(ref _selectedPath, value); }
    public string Status       { get => _status;       private set => SetProperty(ref _status, value); }
    public string TotalDisplay { get => _totalDisplay; private set => SetProperty(ref _totalDisplay, value); }

    public string DuplicateStatus { get => _duplicateStatus; private set => SetProperty(ref _duplicateStatus, value); }
    public string WastedDisplay   { get => _wastedDisplay;   private set => SetProperty(ref _wastedDisplay, value); }

    /// <summary>Tailles minimales proposées pour la recherche de doublons.</summary>
    public string[] MinSizeLabels { get; } = { "100 Ko", "1 Mo", "10 Mo", "100 Mo" };
    public string SelectedMinSizeLabel { get => _selectedMinSizeLabel; set => SetProperty(ref _selectedMinSizeLabel, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AnalyzeCommand.RaiseCanExecuteChanged();
                FindDuplicatesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResults => Results.Count > 0;
    public bool HasDuplicates => Duplicates.Count > 0;

    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand FindDuplicatesCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand OpenPathCommand { get; }

    public string HonestNotice =>
        "Analyse en LECTURE SEULE : CleanSlate calcule la taille de chaque sous-dossier et " +
        "fichier de premier niveau, sans jamais rien supprimer. Cliquez sur une ligne pour " +
        "ouvrir l'emplacement dans l'Explorateur et décider vous-même. Les dossiers protégés " +
        "et les liens symboliques sont ignorés (pas de double-comptage).";

    private async Task AnalyzeAsync()
    {
        var path = SelectedPath?.Trim() ?? string.Empty;
        if (!Directory.Exists(path))
        {
            Status = "Cet emplacement n'existe pas ou n'est pas accessible.";
            return;
        }

        IsBusy = true;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        TotalDisplay = "—";
        Status = "Analyse en cours…";

        var progress = new Progress<string>(m => Status = m);
        try
        {
            var report = await _analyzer.AnalyzeAsync(path, topN: 40, progress, CancellationToken.None);
            long max = report.TopEntries.Count > 0 ? report.TopEntries.Max(e => e.SizeBytes) : 0;
            foreach (var e in report.TopEntries)
                Results.Add(new DiskUsageRowViewModel(e, max));

            TotalDisplay = FileActionLogger.FormatBytes(report.TotalScannedBytes);
            Status = report.TopEntries.Count == 0
                ? "Aucun élément mesurable à cet emplacement."
                : $"{report.TopEntries.Count} élément(s), du plus gros au plus petit. Total analysé : {TotalDisplay}.";
        }
        catch (Exception ex)
        {
            Status = $"Échec de l'analyse : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    public string DuplicateNotice =>
        "Recherche de doublons par CONTENU (empreinte SHA-256), pas seulement par nom : deux " +
        "fichiers ne sont regroupés que s'ils sont strictement identiques. Lecture seule — " +
        "CleanSlate ne supprime rien. Ouvrez chaque copie pour décider laquelle garder.";

    private async Task FindDuplicatesAsync()
    {
        var path = SelectedPath?.Trim() ?? string.Empty;
        if (!Directory.Exists(path))
        {
            DuplicateStatus = "Cet emplacement n'existe pas ou n'est pas accessible.";
            return;
        }

        IsBusy = true;
        Duplicates.Clear();
        OnPropertyChanged(nameof(HasDuplicates));
        WastedDisplay = "—";
        DuplicateStatus = "Recherche des doublons en cours…";

        var progress = new Progress<string>(m => DuplicateStatus = m);
        try
        {
            var report = await _duplicateFinder.FindAsync(
                path, LabelToBytes(SelectedMinSizeLabel), progress, CancellationToken.None);

            foreach (var g in report.Groups)
                Duplicates.Add(new DuplicateGroupViewModel(g));

            WastedDisplay = FileActionLogger.FormatBytes(report.TotalWastedBytes);
            DuplicateStatus = report.Groups.Count == 0
                ? $"Aucun doublon (≥ {SelectedMinSizeLabel}) trouvé — {report.FilesScanned} fichier(s) comparés."
                : $"{report.Groups.Count} groupe(s) de doublons — {WastedDisplay} récupérables au total.";
        }
        catch (Exception ex)
        {
            DuplicateStatus = $"Échec de la recherche : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasDuplicates));
        }
    }

    private static long LabelToBytes(string label) => label switch
    {
        "100 Ko" => 100_000,
        "10 Mo"  => 10_000_000,
        "100 Mo" => 100_000_000,
        _        => 1_000_000, // « 1 Mo » par défaut
    };

    private void Open(DiskUsageRowViewModel? row)
    {
        if (row is null) return;
        if (row.Entry.IsDirectory)
            OpenInShell(row.Path, isDirectory: true);
        else
            OpenInShell(row.Path, isDirectory: false);
    }

    private void OpenPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) OpenInShell(path!, isDirectory: false);
    }

    private void OpenInShell(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Ouvrir l'emplacement", ex.Message);
        }
    }

    private static IEnumerable<string> GetFixedDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName);
        }
        catch { return new[] { @"C:\" }; }
    }
}
