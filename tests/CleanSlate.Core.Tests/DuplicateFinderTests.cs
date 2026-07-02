using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests du détecteur de doublons sur une arborescence temporaire réelle :
/// regroupement par contenu identique, calcul de l'espace gaspillé, et filtre de taille.
/// </summary>
public class DuplicateFinderTests
{
    [Fact]
    public async Task Find_RegroupeLesFichiersIdentiquesEtCalculeLeGaspillage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cs-dup-{Guid.NewGuid():N}");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            var content = new byte[2048];
            for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 251);

            // 3 copies identiques (dont une dans un sous-dossier) + 1 fichier différent.
            File.WriteAllBytes(Path.Combine(root, "a.bin"), content);
            File.WriteAllBytes(Path.Combine(root, "b.bin"), content);
            File.WriteAllBytes(Path.Combine(sub, "c.bin"), content);
            File.WriteAllBytes(Path.Combine(root, "unique.bin"), new byte[2048]); // même taille, contenu différent

            var report = await new DuplicateFinder().FindAsync(root, minSizeBytes: 1, progress: null, ct: default);

            Assert.Single(report.Groups);
            Assert.Equal(3, report.Groups[0].Files.Count);
            Assert.Equal(2048, report.Groups[0].SizeBytes);
            // Espace gaspillé = (3 - 1) * 2048.
            Assert.Equal(2048L * 2, report.Groups[0].WastedBytes);
            Assert.Equal(2048L * 2, report.TotalWastedBytes);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Find_RespecteLaTailleMinimale()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cs-dup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(root, "b.bin"), new byte[10]);

            var report = await new DuplicateFinder().FindAsync(root, minSizeBytes: 1000, progress: null, ct: default);

            Assert.Empty(report.Groups);
            Assert.Equal(0, report.TotalWastedBytes);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TryComputeHash_MemeContenuMemeEmpreinte()
    {
        var f1 = Path.Combine(Path.GetTempPath(), $"cs-h1-{Guid.NewGuid():N}");
        var f2 = Path.Combine(Path.GetTempPath(), $"cs-h2-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(f1, "bonjour");
            File.WriteAllText(f2, "bonjour");
            Assert.Equal(DuplicateFinder.TryComputeHash(f1), DuplicateFinder.TryComputeHash(f2));
        }
        finally { File.Delete(f1); File.Delete(f2); }
    }
}
