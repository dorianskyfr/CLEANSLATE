using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>Tests de la logique de déclenchement de l'entretien automatique.</summary>
public class MaintenanceSchedulerTests
{
    private static readonly DateTime Now = new(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Desactive_NeLancePas()
    {
        Assert.False(MaintenanceScheduler.ShouldRun(false, 24, DateTime.MinValue, Now));
    }

    [Fact]
    public void JamaisLance_EstDu()
    {
        Assert.True(MaintenanceScheduler.ShouldRun(true, 24, DateTime.MinValue, Now));
    }

    [Fact]
    public void IntervalleNonEcoule_NeLancePas()
    {
        Assert.False(MaintenanceScheduler.ShouldRun(true, 24, Now.AddHours(-23), Now));
    }

    [Fact]
    public void IntervalleEcoule_EstDu()
    {
        Assert.True(MaintenanceScheduler.ShouldRun(true, 24, Now.AddHours(-25), Now));
    }

    [Fact]
    public void IntervalleInvalide_RetombeSur24h()
    {
        Assert.False(MaintenanceScheduler.ShouldRun(true, 0, Now.AddHours(-23), Now));
        Assert.True(MaintenanceScheduler.ShouldRun(true, 0, Now.AddHours(-25), Now));
    }
}
