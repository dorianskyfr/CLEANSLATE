using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests d'intégrité du catalogue de bloatware : pas de doublon (un doublon de
/// « Microsoft.MicrosoftSolitaireCollection » s'était glissé dans la liste) et
/// aucune entrée vide.
/// </summary>
public class WindowsDebloatTests
{
    [Fact]
    public void BloatwareCatalog_NeContientAucunDoublon()
    {
        var catalog = WindowsDebloatService.BloatwareCatalog;
        var distinct = catalog.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(distinct, catalog.Count);
    }

    [Fact]
    public void BloatwareCatalog_AucuneEntreeVide()
    {
        Assert.All(WindowsDebloatService.BloatwareCatalog,
            id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void BloatwareCatalog_NonVide()
    {
        Assert.NotEmpty(WindowsDebloatService.BloatwareCatalog);
    }
}
