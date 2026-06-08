using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Diagnostics;
using CleanSlate.Core.Models;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

/// <summary>
/// Représente une catégorie de nettoyage dans l'UI : enveloppe un
/// <see cref="ICleaningProvider"/> et le résultat de son dernier scan.
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    private bool _isSelected;
    private long _recoverableBytes;
    private int _itemCount;
    private bool _hasScanned;

    public CategoryViewModel(ICleaningProvider provider)
    {
        Provider = provider;
        // Coché par défaut uniquement pour les catégories sûres : les catégories
        // « Avertissement » (Prefetch, Corbeille) restent décochées par prudence.
        _isSelected = provider.Severity == CleaningSeverity.Sur;
    }

    public ICleaningProvider Provider { get; }

    public string DisplayName => Provider.DisplayName;
    public string Description => Provider.Description;
    public CleaningSeverity Severity => Provider.Severity;
    public bool RequiresAdministrator => Provider.RequiresAdministrator;

    /// <summary>Éléments détectés au dernier scan (consommés par le nettoyage).</summary>
    public IReadOnlyList<CleanableItem> ScannedItems { get; private set; } = Array.Empty<CleanableItem>();

    /// <summary>Coché par l'utilisateur = inclus dans le scan/nettoyage.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            if (SetProperty(ref _hasScanned, value))
                OnPropertyChanged(nameof(RecoverableDisplay)); // nécessaire si RecoverableBytes reste 0
        }
    }

    public long RecoverableBytes
    {
        get => _recoverableBytes;
        private set
        {
            if (SetProperty(ref _recoverableBytes, value))
                OnPropertyChanged(nameof(RecoverableDisplay));
        }
    }

    public int ItemCount
    {
        get => _itemCount;
        private set => SetProperty(ref _itemCount, value);
    }

    /// <summary>Taille récupérable formatée (ex. « 124 Mo »), pour l'affichage.</summary>
    public string RecoverableDisplay =>
        HasScanned ? FileActionLogger.FormatBytes(RecoverableBytes) : "—";

    /// <summary>Met à jour la VM à partir d'un résultat de scan.</summary>
    public void ApplyScanResult(ScanResult result)
    {
        ScannedItems = result.Items;
        ItemCount = result.ItemCount;
        RecoverableBytes = result.TotalSizeBytes;
        HasScanned = true;
    }

    public void Reset()
    {
        ScannedItems = Array.Empty<CleanableItem>();
        ItemCount = 0;
        RecoverableBytes = 0;
        HasScanned = false;
    }
}
