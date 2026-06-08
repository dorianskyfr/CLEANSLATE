using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la liste blanche de sécurité : le cœur de la confiance dans l'outil.
/// On vérifie que les chemins dangereux sont REFUSÉS.
/// </summary>
public class SafetyTests
{
    [Theory]
    [InlineData(@"C:\")]                      // racine de disque
    [InlineData(@"C:\Windows\System32\x.dll")]// dossier système
    [InlineData(@"C:\Program Files\app.exe")] // programmes installés
    [InlineData("")]                          // vide
    public void RefuseDangerousPaths(string path)
    {
        Assert.False(TestableFileProvider.CheckSafe(path, expectedRoot: null));
    }

    [Fact]
    public void RefusePathOutsideExpectedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanslate-root");
        var outside = Path.Combine(Path.GetTempPath(), "ailleurs", "fichier.tmp");

        // Le fichier est hors de la racine déclarée → refusé (anti-évasion).
        Assert.False(TestableFileProvider.CheckSafe(outside, expectedRoot: root));
    }

    [Fact]
    public void AllowFileInsideExpectedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanslate-root");
        var inside = Path.Combine(root, "sous-dossier", "fichier.tmp");

        Assert.True(TestableFileProvider.CheckSafe(inside, expectedRoot: root));
    }
}
