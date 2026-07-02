using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de l'analyseur d'espace disque sur une arborescence temporaire réelle :
/// calcul des tailles, tri décroissant et limite topN. Lecture seule.
/// </summary>
public class DiskAnalyzerTests
{
    [Fact]
    public async Task Analyze_TrieParTailleDecroissanteEtLimiteTopN()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cs-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // gros dossier (3000 o), petit dossier (10 o), fichier racine (500 o)
            var big = Path.Combine(root, "big");
            var small = Path.Combine(root, "small");
            Directory.CreateDirectory(big);
            Directory.CreateDirectory(small);
            File.WriteAllBytes(Path.Combine(big, "a.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(small, "b.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(root, "root.bin"), new byte[500]);

            var report = await new DiskAnalyzer().AnalyzeAsync(root, topN: 2, progress: null, ct: default);

            Assert.Equal(3510, report.TotalScannedBytes);
            Assert.Equal(2, report.TopEntries.Count);
            Assert.Equal("big", report.TopEntries[0].Name);
            Assert.True(report.TopEntries[0].IsDirectory);
            Assert.Equal(3000, report.TopEntries[0].SizeBytes);
            // Le 2e plus gros est le fichier racine (500) devant "small" (10).
            Assert.Equal("root.bin", report.TopEntries[1].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Analyze_DossierInexistant_RenvoieRapportVide()
    {
        var report = await new DiskAnalyzer().AnalyzeAsync(
            @"Z:\__cleanslate_inexistant__", topN: 10, progress: null, ct: default);

        Assert.Equal(0, report.TotalScannedBytes);
        Assert.Empty(report.TopEntries);
    }

    [Fact]
    public void DirectorySize_SommeRecursive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cs-dsize-{Guid.NewGuid():N}");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "x.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(sub, "y.bin"), new byte[250]);

            Assert.Equal(350, DiskAnalyzer.DirectorySize(root, default));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
