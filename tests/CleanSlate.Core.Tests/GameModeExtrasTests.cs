using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests des nouveautés v1.3 du Mode Jeu : import de profil MSI Afterburner (parsing)
/// et catalogue d'applications à suspendre (profils Léger / Équilibré / Agressif).
/// </summary>
public class GameModeExtrasTests
{
    // ------------------------------------------------------------------
    //  Import MSI Afterburner (parsing du .cfg, conversion kHz → MHz)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseAfterburnerConfig_ConvertitKhzEnMhz()
    {
        const string cfg = """
            [Profile1]
            Format=2
            [VEN_10DE&DEV_2484]
            CoreClkBoost=150000
            MemClkBoost=1000000
            PowerLimit=110
            ThermalLimit=83
            """;

        var import = OverclockingAdvisor.ParseAfterburnerConfig(cfg, "Profile1");

        Assert.NotNull(import);
        Assert.Equal(150, import!.CoreOffsetMhz);    // 150000 kHz → 150 MHz
        Assert.Equal(1000, import.MemoryOffsetMhz);   // 1000000 kHz → 1000 MHz
        Assert.Equal(110, import.PowerLimitPercent);
        Assert.Equal(83, import.TempLimitC);
        Assert.Equal("Profile1", import.SourceProfile);
    }

    [Fact]
    public void ParseAfterburnerConfig_GereLesOffsetsNegatifsEtLesCParsesManquantes()
    {
        const string cfg = """
            [VEN_1002&DEV_73BF]
            CoreClkBoost=-50000
            MemClkBoost=200000
            """;

        var import = OverclockingAdvisor.ParseAfterburnerConfig(cfg, "GPU");

        Assert.NotNull(import);
        Assert.Equal(-50, import!.CoreOffsetMhz);
        Assert.Equal(200, import.MemoryOffsetMhz);
        Assert.Equal(0, import.PowerLimitPercent);
        Assert.Equal(0, import.TempLimitC);
    }

    [Fact]
    public void ParseAfterburnerConfig_AucuneCleConnue_RenvoieNull()
    {
        const string cfg = """
            [Settings]
            Format=2
            Flags=0
            """;

        Assert.Null(OverclockingAdvisor.ParseAfterburnerConfig(cfg, "x"));
    }

    [Fact]
    public void ParseAfterburnerConfig_ContenuVide_RenvoieNull()
    {
        Assert.Null(OverclockingAdvisor.ParseAfterburnerConfig("", "x"));
    }

    // ------------------------------------------------------------------
    //  Catalogue d'applications à suspendre (profils)
    // ------------------------------------------------------------------

    [Fact]
    public void SuspendCatalog_DiscordJamaisListe()
    {
        Assert.DoesNotContain(SuspendCatalog.Apps,
            a => a.ProcessName.Contains("Discord", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuspendCatalog_ProfilsEmboites_LegerInclusDansEquilibreInclusDansAgressif()
    {
        var leger     = SuspendCatalog.ProcessesFor(SuspendTier.Leger);
        var equilibre = SuspendCatalog.ProcessesFor(SuspendTier.Equilibre);
        var agressif  = SuspendCatalog.ProcessesFor(SuspendTier.Agressif);

        Assert.True(leger.Count < equilibre.Count);
        Assert.True(equilibre.Count < agressif.Count);

        Assert.All(leger,     p => Assert.Contains(p, equilibre));
        Assert.All(equilibre, p => Assert.Contains(p, agressif));
    }

    [Fact]
    public void SuspendCatalog_LesNavigateursNeSontQueDansLeProfilAgressif()
    {
        var equilibre = SuspendCatalog.ProcessesFor(SuspendTier.Equilibre);
        var agressif  = SuspendCatalog.ProcessesFor(SuspendTier.Agressif);

        // Chrome n'apparaît pas dans Équilibré (gèle les onglets), mais bien dans Agressif.
        Assert.DoesNotContain("chrome", equilibre);
        Assert.Contains("chrome", agressif);
    }

    [Fact]
    public void GameModeOptions_Default_CorrespondAuProfilEquilibre()
    {
        var defaults = GameModeOptions.Default.ProcessNamesToSuspend;
        var equilibre = SuspendCatalog.ProcessesFor(SuspendTier.Equilibre);

        Assert.Equal(equilibre.OrderBy(x => x), defaults.OrderBy(x => x));
        // Garde-fou : aucun processus système, et Discord absent.
        Assert.DoesNotContain("explorer", defaults);
        Assert.DoesNotContain("Discord", defaults);
    }
}
