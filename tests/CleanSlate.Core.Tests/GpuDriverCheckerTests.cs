using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la logique pure du vérificateur de pilotes : conversion du format de
/// version WMI vers le format NVIDIA, comparaison correcte des versions (NON décimale)
/// et analyse de la taille de téléchargement.
/// </summary>
public class GpuDriverCheckerTests
{
    [Theory]
    [InlineData("32.0.15.6614", "566.14")]
    [InlineData("31.0.15.3713", "537.13")]
    public void ConvertNvidiaVersion_TransformeLeFormatWmi(string wmi, string expected)
    {
        Assert.Equal(expected, GpuDriverChecker.ConvertNvidiaVersion(wmi));
    }

    [Theory]
    [InlineData("toto")]
    [InlineData("")]
    [InlineData("1.2.3")] // pas 4 segments
    public void ConvertNvidiaVersion_FormatInvalide_RenvoieNull(string wmi)
    {
        Assert.Null(GpuDriverChecker.ConvertNvidiaVersion(wmi));
    }

    // Régression : les versions NVIDIA ne sont PAS des décimales.
    // « 566.14 » est plus récent que « 566.9 » (l'ancienne comparaison en double
    // ordonnait 566.14 < 566.9 et ratait donc la mise à jour).
    [Theory]
    [InlineData("566.9", "566.14", true)]
    [InlineData("566.14", "566.36", true)]
    [InlineData("552.44", "566.14", true)]   // majeur plus récent
    [InlineData("566.36", "566.14", false)]  // mineur plus ancien
    [InlineData("566.14", "566.14", false)]  // identique
    public void IsNvidiaNewer_CompareParEntiers(string installed, string latest, bool expected)
    {
        Assert.Equal(expected, GpuDriverChecker.IsNvidiaNewer(installed, latest));
    }

    [Fact]
    public void IsNvidiaNewer_VersionInstalleeInconnue_SignaleLaDerniere()
    {
        Assert.True(GpuDriverChecker.IsNvidiaNewer(null, "566.14"));
        Assert.True(GpuDriverChecker.IsNvidiaNewer("inconnue", "566.14"));
    }

    [Theory]
    [InlineData("350 MB", 350L * 1024 * 1024)]
    [InlineData("1 GB", 1024L * 1024 * 1024)]
    public void ParseSizeToBytes_ConvertitEnOctets(string text, long expected)
    {
        Assert.Equal(expected, GpuDriverChecker.ParseSizeToBytes(text));
    }

    [Fact]
    public void ParseSizeToBytes_TexteVide_RenvoieNull()
    {
        Assert.Null(GpuDriverChecker.ParseSizeToBytes(""));
        Assert.Null(GpuDriverChecker.ParseSizeToBytes(null));
    }
}
