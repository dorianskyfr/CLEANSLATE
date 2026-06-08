using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>Enveloppe une entrée de démarrage avec une bascule activer/désactiver.</summary>
public sealed class StartupEntryViewModel : ObservableObject
{
    private readonly Action<StartupEntryViewModel> _onToggle;
    private bool _isEnabled;

    public StartupEntryViewModel(StartupEntry entry, Action<StartupEntryViewModel> onToggle)
    {
        Entry = entry;
        _isEnabled = entry.IsEnabled;
        _onToggle = onToggle;
        ToggleCommand = new RelayCommand(() => _onToggle(this));
    }

    public StartupEntry Entry { get; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;

    public string LocationLabel => Entry.Location switch
    {
        StartupLocation.RegistryRunCurrentUser => "Registre (utilisateur)",
        StartupLocation.RegistryRunLocalMachine => "Registre (système)",
        StartupLocation.StartupFolder => "Dossier Démarrage",
        StartupLocation.ScheduledTask => "Tâche planifiée",
        _ => "Autre",
    };

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public RelayCommand ToggleCommand { get; }
    public string ToggleLabel => IsEnabled ? "Désactiver" : "Activer";
}
