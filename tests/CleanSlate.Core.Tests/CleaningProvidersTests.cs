using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Cleaning;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests d'intégrité des providers de nettoyage : identifiants uniques, métadonnées
/// non vides, et présence des nouvelles catégories 2.0.
/// </summary>
public class CleaningProvidersTests
{
    private static ICleaningProvider[] AllProviders()
    {
        IActionLogger log = new NullLogger();
        return new ICleaningProvider[]
        {
            new TempFilesProvider(log),
            new BrowserCacheProvider(log),
            new ThumbnailCacheProvider(log),
            new ShaderCacheProvider(log),
            new ErrorReportsProvider(log),
            new WindowsUpdateCacheProvider(log),
            new CrashDumpsProvider(log),
            new WindowsLogsProvider(log),
            new PrefetchProvider(log),
        };
    }

    [Fact]
    public void Providers_OntDesIdentifiantsUniques()
    {
        var ids = AllProviders().Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Providers_OntDesMetadonneesNonVides()
    {
        foreach (var p in AllProviders())
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
        }
    }

    [Fact]
    public void NouveauxProviders_ExposentLesBonnesCategories()
    {
        IActionLogger log = new NullLogger();
        Assert.Equal(CleanSlate.Core.Models.CleaningCategory.CacheWindowsUpdate,
            new WindowsUpdateCacheProvider(log).Category);
        Assert.Equal(CleanSlate.Core.Models.CleaningCategory.VidagesPlantage,
            new CrashDumpsProvider(log).Category);
    }
}
