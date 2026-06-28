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

    // --- Détection « orpheline » : ne jamais signaler une commande indécidable ---

    [Theory]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL")] // nom nu résolu via PATH
    [InlineData("powershell.exe -NoProfile")]               // nom nu
    [InlineData("OneDrive.exe /background")]                // nom nu
    public void IsOrphanedRunCommand_NomNu_NeSignaleJamais(string command)
    {
        // Un exécutable sans chemin absolu est résolu via le PATH : indécidable,
        // on ne doit donc JAMAIS le proposer à la suppression (sinon faux positif).
        Assert.False(RegistryCleaner.IsOrphanedRunCommand(command, out _));
    }

    [Fact]
    public void IsOrphanedRunCommand_CheminAbsoluInexistant_EstSignale()
    {
        var bogus = @"C:\CleanSlate\__inexistant__\nope.exe";
        Assert.True(RegistryCleaner.IsOrphanedRunCommand($"\"{bogus}\" -arg", out var exe));
        Assert.Equal(bogus, exe);
    }

    [Fact]
    public void IsOrphanedRunCommand_VariableEnvironnement_EstDeveloppee()
    {
        // %SystemRoot%\...\__inexistant__.exe doit être développé puis testé en absolu.
        Assert.True(RegistryCleaner.IsOrphanedRunCommand(
            @"%SystemRoot%\System32\__cleanslate_inexistant__.exe", out var exe));
        Assert.DoesNotContain("%", exe);
    }
}
