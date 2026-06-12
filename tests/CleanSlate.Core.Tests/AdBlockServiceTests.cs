using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la logique pure de nettoyage de l'ancien blocage par fichier hosts
/// (versions <= v0.9.2), remplacé par le bloqueur de pub DNS (AdGuard).
/// </summary>
public class AdBlockServiceTests
{
    private const string Start = "# ==== CleanSlate AdBlock START ====";
    private const string End   = "# ==== CleanSlate AdBlock END ====";

    [Fact]
    public void StripLegacyBlock_SupprimeLeBlocEntreLesMarqueurs()
    {
        var hosts =
            "127.0.0.1 localhost\n" +
            "::1 localhost\n\n" +
            $"{Start}\n" +
            "0.0.0.0 ads.example.com\n" +
            "0.0.0.0 tracker.example.com\n" +
            $"{End}\n";

        var cleaned = DnsAdBlockService.StripLegacyBlock(hosts);

        Assert.DoesNotContain(Start, cleaned);
        Assert.DoesNotContain(End, cleaned);
        Assert.DoesNotContain("ads.example.com", cleaned);
        Assert.Contains("127.0.0.1 localhost", cleaned);
        Assert.Contains("::1 localhost", cleaned);
    }

    [Fact]
    public void StripLegacyBlock_GereUnBlocSansMarqueurDeFin()
    {
        var hosts =
            "127.0.0.1 localhost\n\n" +
            $"{Start}\n" +
            "0.0.0.0 ads.example.com\n";

        var cleaned = DnsAdBlockService.StripLegacyBlock(hosts);

        Assert.DoesNotContain(Start, cleaned);
        Assert.DoesNotContain("ads.example.com", cleaned);
        Assert.Contains("127.0.0.1 localhost", cleaned);
    }

    [Fact]
    public void StripLegacyBlock_AucunMarqueur_RetourneInchange()
    {
        var hosts = "127.0.0.1 localhost\n::1 localhost\n";

        var cleaned = DnsAdBlockService.StripLegacyBlock(hosts);

        Assert.Equal(hosts, cleaned);
    }

    // ------------------------------------------------------------------
    //  Sauvegarde DNS (fournisseur choisi + DNS d'origine par adaptateur)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseBackup_FormatActuel_RelitFournisseurEtAdaptateurs()
    {
        var json = DnsAdBlockService.SerializeBackup("quad9", new Dictionary<string, string[]>
        {
            ["{ADAPTER-1}"] = new[] { "192.168.1.1" },
            ["{ADAPTER-2}"] = Array.Empty<string>(),
        });

        var (provider, adapters) = DnsAdBlockService.ParseBackup(json);

        Assert.Equal("quad9", provider);
        Assert.Equal(2, adapters.Count);
        Assert.Equal(new[] { "192.168.1.1" }, adapters["{ADAPTER-1}"]);
        Assert.Empty(adapters["{ADAPTER-2}"]);
    }

    [Fact]
    public void ParseBackup_AncienFormat_DictionnaireSimple_FournisseurAdGuard()
    {
        // Format des versions <= v1.1.5 : dictionnaire adaptateur → DNS, sans fournisseur.
        const string legacy = """{"{ADAPTER-1}":["8.8.8.8","8.8.4.4"]}""";

        var (provider, adapters) = DnsAdBlockService.ParseBackup(legacy);

        Assert.Equal("adguard", provider);
        Assert.Equal(new[] { "8.8.8.8", "8.8.4.4" }, adapters["{ADAPTER-1}"]);
    }

    [Fact]
    public void AllProviders_ContiennentAdGuardEnPremier_AvecDnsValides()
    {
        Assert.Equal("adguard", DnsAdBlockService.AllProviders[0].Id);
        Assert.All(DnsAdBlockService.AllProviders, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Primary));
            Assert.False(string.IsNullOrWhiteSpace(p.Secondary));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
        });
    }
}
