using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Sous-catégorie « Overclocking » du Mode Jeu. Détecte la carte graphique et
/// propose plusieurs profils (Sûr / Équilibré / Performance). Sur les cartes NVIDIA
/// et AMD dédiées, l'overclock peut être appliqué automatiquement (NVAPI / ADL
/// OverdriveN) avec un Reset ; sur les autres cartes (Intel notamment), le profil
/// sélectionné est guidé pas à pas dans l'outil du constructeur.
/// </summary>
public sealed class OverclockingViewModel : ObservableObject
{
    private readonly IOverclockingAdvisor _advisor;
    private readonly IGpuOverclocker _overclocker;
    private readonly IGpuDriverChecker _driverChecker;
    private readonly IDialogService _dialogs;

    private GpuInfo? _selectedGpu;
    private OverclockProfile? _selectedProfile;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isCheckingDriver;
    private GpuDriverCheckResult? _driverCheck;

    public OverclockingViewModel(IOverclockingAdvisor advisor, IGpuOverclocker overclocker, IGpuDriverChecker driverChecker, IDialogService dialogs)
    {
        _advisor = advisor;
        _overclocker = overclocker;
        _driverChecker = driverChecker;
        _dialogs = dialogs;

        RefreshCommand           = new RelayCommand(DetectGpus, () => !IsBusy);
        CopyProfileCommand       = new RelayCommand(CopyProfile, () => SelectedProfile is { Actionable: true });
        ApplyCommand             = new AsyncRelayCommand(ApplyAsync, () => SelectedProfile is { Actionable: true } && CanAutoApply && !IsBusy);
        ResetCommand             = new AsyncRelayCommand(ResetAsync, () => CanAutoApply && !IsBusy);
        CheckDriverUpdateCommand = new AsyncRelayCommand(CheckDriverUpdateAsync, () => SelectedGpu is not null && !IsCheckingDriver);
        OpenLatestDriverCommand  = new RelayCommand(() => Open(_driverCheck!.DownloadUrl!), () => !string.IsNullOrEmpty(_driverCheck?.DownloadUrl));

        DetectGpus();
    }

    public ObservableCollection<GpuInfo> Gpus { get; } = new();
    public ObservableCollection<OverclockProfile> Profiles { get; } = new();

    public RelayCommand RefreshCommand            { get; }
    public RelayCommand CopyProfileCommand        { get; }
    public AsyncRelayCommand ApplyCommand         { get; }
    public AsyncRelayCommand ResetCommand         { get; }
    public AsyncRelayCommand CheckDriverUpdateCommand { get; }
    public RelayCommand OpenLatestDriverCommand   { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
                ResetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCheckingDriver
    {
        get => _isCheckingDriver;
        private set
        {
            if (SetProperty(ref _isCheckingDriver, value))
                CheckDriverUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasDriverCheckResult => _driverCheck is not null;
    public string DriverCheckMessage => _driverCheck?.Message ?? string.Empty;
    public bool DriverUpdateAvailable => _driverCheck?.UpdateAvailable ?? false;
    public string LatestDriverVersion => _driverCheck?.LatestVersion ?? "—";
    public string LatestDriverDate    => _driverCheck?.LatestReleaseDate ?? "—";
    public string LatestDriverSize    => _driverCheck?.DownloadSizeDisplay ?? "—";
    public bool CanOpenLatestDriver   => !string.IsNullOrEmpty(_driverCheck?.DownloadUrl);
    public string OpenLatestDriverLabel => DriverUpdateAvailable
        ? "⬇️ Télécharger le pilote NVIDIA"
        : "🌐 Ouvrir la page des pilotes";

    /// <summary>Vrai si l'application automatique de l'overclock est possible (NVIDIA ou AMD dédiée).</summary>
    public bool CanAutoApply => SelectedGpu is not null && _overclocker.CanApply(SelectedGpu);

    public string AutoApplyNote => CanAutoApply
        ? "✅ Carte compatible : l'overclock peut être appliqué automatiquement (bouton « Appliquer l'overclock »). " +
          "Un bouton « Reset » remet tout à zéro à tout moment."
        : "ℹ️ Application automatique non disponible pour cette carte — suivez les étapes guidées du profil " +
          "sélectionné dans l'outil du constructeur (le profil reste optimal).";

    public GpuInfo? SelectedGpu
    {
        get => _selectedGpu;
        set
        {
            if (SetProperty(ref _selectedGpu, value))
            {
                OnPropertyChanged(nameof(GpuName));
                OnPropertyChanged(nameof(GpuDetails));
                OnPropertyChanged(nameof(HasGpu));
                OnPropertyChanged(nameof(CanAutoApply));
                OnPropertyChanged(nameof(AutoApplyNote));
                ResetCommand.RaiseCanExecuteChanged();
                ClearDriverCheck();
                CheckDriverUpdateCommand.RaiseCanExecuteChanged();

                Profiles.Clear();
                if (value is not null)
                    foreach (var p in _advisor.RecommendProfiles(value, CanAutoApply)) Profiles.Add(p);
                OnPropertyChanged(nameof(HasProfiles));

                SelectedProfile = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
            }
        }
    }

    public OverclockProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(HasProfile));
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
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<string> Steps { get; } = new();

    public bool   HasGpu      => SelectedGpu is not null;
    public bool   HasProfile  => SelectedProfile is not null;
    public bool   HasProfiles => Profiles.Count > 1;
    public string GpuName     => SelectedGpu?.Name ?? "Aucune carte graphique détectée";
    public string GpuDetails  => SelectedGpu is null
        ? string.Empty
        : $"VRAM : {SelectedGpu.VramDisplay}   •   Pilote : {SelectedGpu.DriverVersion ?? "?"}";

    public string CoreOffsetDisplay   => SelectedProfile is null ? "—" : $"+{SelectedProfile.CoreOffsetMhz} MHz";
    public string MemoryOffsetDisplay => SelectedProfile is null ? "—" : $"+{SelectedProfile.MemoryOffsetMhz} MHz";
    public string PowerLimitDisplay   => SelectedProfile is null ? "—" : $"{SelectedProfile.PowerLimitPercent} %";
    public string TempLimitDisplay    => SelectedProfile is null ? "—" : $"{SelectedProfile.TempLimitC} °C";
    public string FanStrategy         => SelectedProfile?.FanStrategy ?? "—";
    public string Rationale           => SelectedProfile?.Rationale ?? string.Empty;

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

    private async Task ApplyAsync()
    {
        if (SelectedGpu is null || SelectedProfile is null) return;

        var profile = SelectedProfile;
        var confirmed = _dialogs.Confirm(
            "Appliquer l'overclock",
            $"CleanSlate va appliquer le profil « {profile.Name} » à votre {SelectedGpu.Name} :\n\n" +
            $"  • Fréquence cœur : +{profile.CoreOffsetMhz} MHz\n" +
            $"  • Fréquence mémoire : +{profile.MemoryOffsetMhz} MHz\n\n" +
            "⚠️ EXPÉRIMENTAL. En cas d'instabilité (artefacts, écran noir, crash), cliquez sur " +
            "« Reset » pour tout annuler — les offsets ne survivent pas à un redémarrage. " +
            "Lancez ensuite un test de stabilité.\n\nContinuer ?");
        if (!confirmed) return;

        IsBusy = true;
        Status = "Application de l'overclock…";
        try
        {
            var gpu = SelectedGpu;
            var result = await Task.Run(() => _overclocker.Apply(gpu, profile));
            Status = result.Message;
            if (result.Success)
                _dialogs.Info("Overclock appliqué",
                    result.Message + "\n\nLancez maintenant un test de stabilité (benchmark ou jeu exigeant) " +
                    "20-30 min. En cas de souci, cliquez sur « Reset ».");
            else
                _dialogs.Warn("Overclock non appliqué", result.Message);
        }
        catch (Exception ex)
        {
            Status = $"Erreur : {ex.Message}";
            _dialogs.Warn("Overclock", ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task ResetAsync()
    {
        if (SelectedGpu is null) return;
        IsBusy = true;
        Status = "Réinitialisation des offsets…";
        try
        {
            var gpu = SelectedGpu;
            var result = await Task.Run(() => _overclocker.Reset(gpu));
            Status = result.Message;
            if (!result.Success) _dialogs.Warn("Reset", result.Message);
        }
        catch (Exception ex)
        {
            Status = $"Erreur : {ex.Message}";
            _dialogs.Warn("Reset", ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task CheckDriverUpdateAsync()
    {
        if (SelectedGpu is null) return;
        IsCheckingDriver = true;
        ClearDriverCheck();
        Status = "Recherche du dernier pilote auprès du fabricant…";
        try
        {
            var gpu = SelectedGpu;
            _driverCheck = await _driverChecker.CheckLatestAsync(gpu, CancellationToken.None);
            Status = _driverCheck.Message;
        }
        catch (Exception ex)
        {
            _driverCheck = null;
            Status = $"Erreur : {ex.Message}";
            _dialogs.Warn("Vérification du pilote", ex.Message);
        }
        finally
        {
            IsCheckingDriver = false;
            RaiseDriverCheckChanged();
        }
    }

    private void ClearDriverCheck()
    {
        _driverCheck = null;
        RaiseDriverCheckChanged();
    }

    private void RaiseDriverCheckChanged()
    {
        OnPropertyChanged(nameof(HasDriverCheckResult));
        OnPropertyChanged(nameof(DriverCheckMessage));
        OnPropertyChanged(nameof(DriverUpdateAvailable));
        OnPropertyChanged(nameof(LatestDriverVersion));
        OnPropertyChanged(nameof(LatestDriverDate));
        OnPropertyChanged(nameof(LatestDriverSize));
        OnPropertyChanged(nameof(CanOpenLatestDriver));
        OnPropertyChanged(nameof(OpenLatestDriverLabel));
        OpenLatestDriverCommand.RaiseCanExecuteChanged();
    }

    private void CopyProfile()
    {
        if (SelectedProfile is null) return;
        var profile = SelectedProfile;
        var sb = new StringBuilder();
        sb.AppendLine($"Profil d'overclocking « {profile.Name} » — {GpuName}");
        sb.AppendLine($"  Core Clock   : {CoreOffsetDisplay}");
        sb.AppendLine($"  Memory Clock : {MemoryOffsetDisplay}");
        sb.AppendLine($"  Power Limit  : {PowerLimitDisplay}");
        sb.AppendLine($"  Temp Limit   : {TempLimitDisplay}");
        sb.AppendLine($"  Ventilation  : {FanStrategy}");
        sb.AppendLine();
        sb.AppendLine("Étapes :");
        int n = 1;
        foreach (var s in profile.Steps) sb.AppendLine($"  {n++}. {s}");

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
