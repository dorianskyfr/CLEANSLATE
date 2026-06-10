using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Sous-catégorie « Overclocking » du Mode Jeu. Détecte la carte graphique et
/// propose un profil de départ équilibré (performance vs stabilité), à appliquer
/// dans l'outil officiel du constructeur. On n'applique jamais l'overclock
/// directement (voir IOverclockingAdvisor pour l'explication honnête).
/// </summary>
public sealed class OverclockingViewModel : ObservableObject
{
    private readonly IOverclockingAdvisor _advisor;
    private readonly IDialogService _dialogs;

    private GpuInfo? _selectedGpu;
    private OverclockProfile? _profile;
    private string _status = string.Empty;

    public OverclockingViewModel(IOverclockingAdvisor advisor, IDialogService dialogs)
    {
        _advisor = advisor;
        _dialogs = dialogs;

        RefreshCommand          = new RelayCommand(DetectGpus);
        CopyProfileCommand      = new RelayCommand(CopyProfile, () => Profile is { Recommended: true });
        OpenAfterburnerCommand  = new RelayCommand(() => Open("https://www.msi.com/Landing/afterburner/graphics-cards"));

        DetectGpus();
    }

    public ObservableCollection<GpuInfo> Gpus { get; } = new();

    public RelayCommand RefreshCommand         { get; }
    public RelayCommand CopyProfileCommand     { get; }
    public RelayCommand OpenAfterburnerCommand { get; }

    public GpuInfo? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (SetProperty(ref _selectedGpu, value))
            {
                Profile = value is null ? null : _advisor.Recommend(value);
                OnPropertyChanged(nameof(GpuName));
                OnPropertyChanged(nameof(GpuDetails));
                OnPropertyChanged(nameof(HasGpu));
            }
        }
    }

    public OverclockProfile? Profile
    {
        get => _profile;
        private set
        {
            if (SetProperty(ref _profile, value))
            {
                OnPropertyChanged(nameof(HasProfile));
                OnPropertyChanged(nameof(Recommended));
                OnPropertyChanged(nameof(CoreOffsetDisplay));
                OnPropertyChanged(nameof(MemoryOffsetDisplay));
                OnPropertyChanged(nameof(PowerLimitDisplay));
                OnPropertyChanged(nameof(TempLimitDisplay));
                OnPropertyChanged(nameof(FanStrategy));
                OnPropertyChanged(nameof(Rationale));
                Steps.Clear();
                if (value is not null)
                    foreach (var s in value.Steps) Steps.Add(s);
                CopyProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<string> Steps { get; } = new();

    public bool   HasGpu      => SelectedGpu is not null;
    public bool   HasProfile  => Profile is not null;
    public bool   Recommended => Profile is { Recommended: true };
    public string GpuName     => SelectedGpu?.Name ?? "Aucune carte graphique détectée";
    public string GpuDetails  => SelectedGpu is null
        ? string.Empty
        : $"VRAM : {SelectedGpu.VramDisplay}   •   Pilote : {SelectedGpu.DriverVersion ?? "?"}";

    public string CoreOffsetDisplay   => Profile is null ? "—" : $"+{Profile.CoreOffsetMhz} MHz";
    public string MemoryOffsetDisplay => Profile is null ? "—" : $"+{Profile.MemoryOffsetMhz} MHz";
    public string PowerLimitDisplay   => Profile is null ? "—" : $"{Profile.PowerLimitPercent} %";
    public string TempLimitDisplay    => Profile is null ? "—" : $"{Profile.TempLimitC} °C";
    public string FanStrategy         => Profile?.FanStrategy ?? "—";
    public string Rationale           => Profile?.Rationale ?? string.Empty;

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private void DetectGpus()
    {
        Gpus.Clear();
        try
        {
            foreach (var g in _advisor.DetectGpus()) Gpus.Add(g);
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Détection GPU", ex.Message);
        }

        SelectedGpu = Gpus.FirstOrDefault();
        Status = Gpus.Count == 0
            ? "Aucune carte graphique détectée."
            : $"{Gpus.Count} carte(s) graphique(s) détectée(s).";
    }

    private void CopyProfile()
    {
        if (Profile is null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"Profil d'overclocking recommandé — {GpuName}");
        sb.AppendLine($"  Core Clock   : {CoreOffsetDisplay}");
        sb.AppendLine($"  Memory Clock : {MemoryOffsetDisplay}");
        sb.AppendLine($"  Power Limit  : {PowerLimitDisplay}");
        sb.AppendLine($"  Temp Limit   : {TempLimitDisplay}");
        sb.AppendLine($"  Ventilation  : {FanStrategy}");
        sb.AppendLine();
        sb.AppendLine("Étapes :");
        int n = 1;
        foreach (var s in Profile.Steps) sb.AppendLine($"  {n++}. {s}");

        try
        {
            Clipboard.SetText(sb.ToString());
            Status = "Profil copié dans le presse-papiers. ✅";
        }
        catch (Exception ex)
        {
            _dialogs.Warn("Copie impossible", ex.Message);
        }
    }

    private void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _dialogs.Warn("Ouverture impossible", ex.Message); }
    }
}
