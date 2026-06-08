using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la logique pure d'extraction du chemin d'exécutable depuis une
/// commande de registre Run (gestion des guillemets et des arguments).
/// </summary>
public class RegistryCleanerTests
{
    [Theory]
    [InlineData(@"C:\App\app.exe", @"C:\App\app.exe")]
    [InlineData(@"C:\App\app.exe -minimized", @"C:\App\app.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" /background",
                @"C:\Program Files\App\app.exe")]
    [InlineData("\"C:\\Dossier avec espaces\\x.exe\"", @"C:\Dossier avec espaces\x.exe")]
    public void ExtractExecutablePath_ExtraitLeBonChemin(string command, string expected)
    {
        Assert.Equal(expected, RegistryCleaner.ExtractExecutablePath(command));
    }

    [Fact]
    public void ExtractExecutablePath_ChaineVide_RetourneNull()
    {
        Assert.Null(RegistryCleaner.ExtractExecutablePath("   "));
    }
}
