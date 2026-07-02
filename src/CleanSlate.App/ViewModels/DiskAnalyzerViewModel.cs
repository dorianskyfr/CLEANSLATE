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

/// <summary>
/// Analyseur d'espace disque (lecture seule) : choisissez un lecteur ou un dossier,
/// CleanSlate liste ses plus gros sous-dossiers/fichiers pour trouver ce qui remplit
/// le disque. Rien n'est jamais supprimé ici — un clic ouvre l'emplacement dans
/// l'Explorateur pour agir vous-même.
/// </summary>
public sealed class DiskAnalyzerViewModel : ObservableObject
{
    private readonly IDiskAnalyzer _analyzer;
    private readonly IDialogService _dialogs;

    private string _selectedPath;
    private string _status = "Choisissez un lecteur ou saisissez un dossier, puis lancez l'analyse.";
    private string _totalDisplay = "—";
    private bool _isBusy;

    public DiskAnalyzerViewModel(IDiskAnalyzer analyzer, IDialogService dialogs)
    {
        _analyzer = analyzer;
        _dialogs  = dialogs;

        Drives = new ObservableCollection<string>(GetFixedDrives());
        _selectedPath = Drives.FirstOrDefault() ?? @"C:\";

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy);
        OpenCommand    = new RelayCommand<DiskUsageRowViewModel>(Open);
    }

    public ObservableCollection<string> Drives { get; }
    public ObservableCollection<DiskUsageRowViewModel> Results { get; } = new();

    public string SelectedPath { get => _selectedPath; set => SetProperty(ref _selectedPath, value); }
    public string Status       { get => _status;       private set => SetProperty(ref _status, value); }
    public string TotalDisplay { get => _totalDisplay; private set => SetProperty(ref _totalDisplay, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) AnalyzeCommand.RaiseCanExecuteChanged(); }
    }

    public bool HasResults => Results.Count > 0;

    public AsyncRelayCommand AnalyzeCommand { get; }
    public ICommand OpenCommand { get; }

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

    private void Open(DiskUsageRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            if (row.Entry.IsDirectory)
                Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
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
