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
}
