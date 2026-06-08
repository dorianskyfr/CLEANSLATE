using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests d'intégration légers du flux scan → clean sur un dossier temporaire réel,
/// en vérifiant que le scan ne supprime RIEN et que le clean libère bien l'espace.
/// </summary>
public class CleaningFlowTests : IDisposable
{
    private readonly string _root;

    public CleaningFlowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanslate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Scan_NeSupprimeRien_EtListeLesFichiers()
    {
        var f1 = CreateFile("a.tmp", 100);
        var f2 = CreateFile(Path.Combine("sub", "b.tmp"), 250);

        var provider = new TestableFileProvider(new NullLogger(), _root);
        var result = await provider.ScanAsync(progress: null, CancellationToken.None);

        Assert.Equal(2, result.ItemCount);
        Assert.Equal(350, result.TotalSizeBytes);
        // Le scan ne supprime rien.
        Assert.True(File.Exists(f1));
        Assert.True(File.Exists(f2));
    }

    [Fact]
    public async Task Clean_SupprimeLesFichiersScannes_EtLibereEspace()
    {
        CreateFile("a.tmp", 100);
        CreateFile("c.tmp", 400);

        var provider = new TestableFileProvider(new NullLogger(), _root);
        var scan = await provider.ScanAsync(null, CancellationToken.None);
        var clean = await provider.CleanAsync(scan.Items, null, CancellationToken.None);

        Assert.Equal(2, clean.DeletedCount);
        Assert.Equal(0, clean.FailedCount);
        Assert.Equal(500, clean.FreedBytes);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    private string CreateFile(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
