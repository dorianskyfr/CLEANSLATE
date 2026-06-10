using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la logique pure de l'Overclocking : profils multiples, filtrage des
/// écrans virtuels, et prise en charge des iGPU Intel récents (Iris Xe / Arc).
/// </summary>
public class OverclockingAdvisorTests
{
    private static readonly OverclockingAdvisor Advisor = new();

    [Theory]
    [InlineData("Parsec Virtual Display Adapter")]
    [InlineData("Virtual Display Driver")]
    [InlineData("spacedesk Virtual Display")]
    public void IsVirtualAdapter_DetecteLesEcransVirtuels(string name)
    {
        Assert.True(OverclockingAdvisor.IsVirtualAdapter(name));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070")]
    [InlineData("AMD Radeon RX 6700 XT")]
    [InlineData("Intel(R) Iris(R) Xe Graphics")]
    public void IsVirtualAdapter_IgnoreLesVraiesCartes(string name)
    {
        Assert.False(OverclockingAdvisor.IsVirtualAdapter(name));
    }

    [Theory]
    [InlineData("Intel(R) Iris(R) Xe Graphics")]
    [InlineData("Intel(R) Arc(TM) Graphics")]
    public void IsTunableIntelIGpu_ReconnaitIrisXeEtArc(string name)
    {
        Assert.True(OverclockingAdvisor.IsTunableIntelIGpu(name));
    }

    [Theory]
    [InlineData("Intel(R) UHD Graphics 620")]
    [InlineData("Intel(R) HD Graphics 520")]
    public void IsTunableIntelIGpu_RefuseLesAnciensIGpu(string name)
    {
        Assert.False(OverclockingAdvisor.IsTunableIntelIGpu(name));
    }

    [Fact]
    public void RecommendProfiles_IrisXe_RenvoieDesProfilsActionnables()
    {
        var gpu = new GpuInfo("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, 0, "31.0.101.5333", IsIntegrated: true);

        var profiles = Advisor.RecommendProfiles(gpu, canAutoApply: false);

        Assert.True(profiles.Count > 1);
        Assert.All(profiles, p => Assert.True(p.Actionable));
        Assert.Single(profiles, p => p.IsDefault);
    }

    [Fact]
    public void RecommendProfiles_AncienIGpuIntel_RenvoieUnSeulProfilNonActionnable()
    {
        var gpu = new GpuInfo("Intel(R) UHD Graphics 620", GpuVendor.Intel, 0, "27.20.100.9466", IsIntegrated: true);

        var profiles = Advisor.RecommendProfiles(gpu, canAutoApply: false);

        var profile = Assert.Single(profiles);
        Assert.False(profile.Actionable);
        Assert.True(profile.IsDefault);
    }

    [Theory]
    [InlineData(GpuVendor.Nvidia)]
    [InlineData(GpuVendor.Amd)]
    [InlineData(GpuVendor.Intel)]
    [InlineData(GpuVendor.Unknown)]
    public void RecommendProfiles_CarteDediee_RenvoiePlusieursProfilsAvecUnDefaut(GpuVendor vendor)
    {
        var gpu = new GpuInfo("Carte de test", vendor, 8L * 1024 * 1024 * 1024, "1.0.0.0", IsIntegrated: false);

        var profiles = Advisor.RecommendProfiles(gpu, canAutoApply: vendor == GpuVendor.Nvidia);

        Assert.True(profiles.Count >= 3);
        Assert.All(profiles, p => Assert.True(p.Actionable));
        Assert.Single(profiles, p => p.IsDefault);
    }

    [Fact]
    public void RecommendProfiles_Nvidia_LesEtapesMentionnentLApplicationAutomatiqueSiDisponible()
    {
        var gpu = new GpuInfo("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 12L * 1024 * 1024 * 1024, "5.6.7.8", IsIntegrated: false);

        var withAuto = Advisor.RecommendProfiles(gpu, canAutoApply: true);
        var withoutAuto = Advisor.RecommendProfiles(gpu, canAutoApply: false);

        Assert.Contains(withAuto[0].Steps, s => s.Contains("Appliquer l'overclock"));
        Assert.DoesNotContain(withoutAuto[0].Steps, s => s.Contains("Appliquer l'overclock"));
    }

    [Fact]
    public void RecommendProfiles_Amd_LesEtapesMentionnentLApplicationAutomatiqueSiDisponible()
    {
        var gpu = new GpuInfo("AMD Radeon RX 6700 XT", GpuVendor.Amd, 12L * 1024 * 1024 * 1024, "31.0.0.0", IsIntegrated: false);

        var withAuto = Advisor.RecommendProfiles(gpu, canAutoApply: true);
        var withoutAuto = Advisor.RecommendProfiles(gpu, canAutoApply: false);

        Assert.Contains(withAuto[0].Steps, s => s.Contains("Appliquer l'overclock"));
        Assert.DoesNotContain(withoutAuto[0].Steps, s => s.Contains("Appliquer l'overclock"));
    }
}
