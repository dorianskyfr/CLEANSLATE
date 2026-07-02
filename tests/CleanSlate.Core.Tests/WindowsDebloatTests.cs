using System.Text.Json;
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

    // --- Réversibilité ---

    [Fact]
    public void ClassifyRegistryValue_DistingueDwordStringEtAbsent()
    {
        var sentinel = new object();

        Assert.Equal(("DWord", "5", true), WindowsDebloatService.ClassifyRegistryValue(5, sentinel));
        Assert.Equal(("String", "Deny", true), WindowsDebloatService.ClassifyRegistryValue("Deny", sentinel));
        Assert.Equal(((string?)null, (string?)null, false),
            WindowsDebloatService.ClassifyRegistryValue(sentinel, sentinel));
        Assert.Equal(((string?)null, (string?)null, false),
            WindowsDebloatService.ClassifyRegistryValue(null, sentinel));
    }

    [Fact]
    public void DebloatBackup_SerialisationRoundTrip()
    {
        var backup = new DebloatBackup
        {
            Registry = { new RegistryValueBackup(@"HKEY_LOCAL_MACHINE\SOFTWARE\X", "Y", "DWord", "0", true) },
            Services = { new ServiceStartBackup("DiagTrack", 2, true) },
            DisabledTasks = { @"\Microsoft\Windows\Foo\Bar" },
        };

        var json = JsonSerializer.Serialize(backup);
        var round = JsonSerializer.Deserialize<DebloatBackup>(json)!;

        Assert.Single(round.Registry);
        Assert.Equal("DWord", round.Registry[0].Kind);
        Assert.True(round.Registry[0].Existed);
        Assert.Equal(2, round.Services[0].PreviousStart);
        Assert.Equal(@"\Microsoft\Windows\Foo\Bar", round.DisabledTasks[0]);
    }

    [Fact]
    public void HasBackup_RefleteLaPresenceDuFichier()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cs-debloat-{Guid.NewGuid():N}.json");
        var svc = new WindowsDebloatService(new NullLogger(), backupFile: path);
        try
        {
            Assert.False(svc.HasBackup);
            File.WriteAllText(path, JsonSerializer.Serialize(new DebloatBackup
            {
                Registry = { new RegistryValueBackup(@"HKEY_CURRENT_USER\X", "Y", "DWord", "1", false) },
            }));
            Assert.True(svc.HasBackup);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
