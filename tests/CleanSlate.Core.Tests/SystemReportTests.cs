using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>Tests du générateur de rapport système (contenu attendu).</summary>
public class SystemReportTests
{
    [Fact]
    public void Build_ContientLesInfosCles()
    {
        var info = new SystemOverview(
            OsName: "Windows 11 Pro",
            CpuName: "Test CPU",
            LogicalCores: 8,
            TotalRamBytes: 16UL * 1024 * 1024 * 1024,
            Uptime: TimeSpan.FromHours(50),
            Drives: new[] { new DriveOverview("C:", "Système", 500_000_000_000, 100_000_000_000) },
            MemoryLoadPercent: 42);

        var health = HealthScore.Evaluate(new HealthInputs(2, 20, 42));
        var report = SystemReport.Build(info, health, new DateTime(2026, 7, 2, 14, 30, 0));

        Assert.Contains("Rapport système CleanSlate", report);
        Assert.Contains("Windows 11 Pro", report);
        Assert.Contains("Test CPU", report);
        Assert.Contains($"{health.Score}/100", report);
        Assert.Contains("C:", report);
        Assert.Contains("2026-07-02 14:30", report);
    }
}
