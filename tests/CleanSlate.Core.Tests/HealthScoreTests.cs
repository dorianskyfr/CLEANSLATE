using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>Tests du score de santé : agrégation transparente des signaux + conseils.</summary>
public class HealthScoreTests
{
    [Fact]
    public void SystemeSain_Score100SansConseil()
    {
        var r = HealthScore.Evaluate(new HealthInputs(UptimeDays: 1, MinFreeDiskPercent: 60, RamLoadPercent: 40));
        Assert.Equal(100, r.Score);
        Assert.Equal("Excellent", r.Rating);
        Assert.Empty(r.Tips);
    }

    [Fact]
    public void DisquePresquePlein_PenaliseEtConseille()
    {
        var r = HealthScore.Evaluate(new HealthInputs(1, MinFreeDiskPercent: 5, RamLoadPercent: 40));
        Assert.Equal(75, r.Score); // -25
        Assert.NotEmpty(r.Tips);
    }

    [Fact]
    public void CumulDeProblemes_AdditionneLesPenalites()
    {
        // disque < 10 (-25), RAM > 90 (-15), uptime >= 14 (-15) => 45
        var r = HealthScore.Evaluate(new HealthInputs(UptimeDays: 20, MinFreeDiskPercent: 8, RamLoadPercent: 95));
        Assert.Equal(45, r.Score);
        Assert.Equal("Moyen", r.Rating);
        Assert.Equal(3, r.Tips.Count);
    }

    [Fact]
    public void ScoreBorneEntre0Et100()
    {
        var pire = HealthScore.Evaluate(new HealthInputs(UptimeDays: 100, MinFreeDiskPercent: 0, RamLoadPercent: 100));
        Assert.InRange(pire.Score, 0, 100);
    }

    [Theory]
    [InlineData(90, "Excellent")]
    [InlineData(70, "Bon")]
    [InlineData(50, "Moyen")]
    [InlineData(20, "À surveiller")]
    public void RatingFor_ClasseCorrectement(int score, string expected)
    {
        Assert.Equal(expected, HealthScore.RatingFor(score));
    }
}
