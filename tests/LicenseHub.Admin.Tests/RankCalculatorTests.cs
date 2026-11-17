using LicenseHub.Admin.Dashboard;

namespace LicenseHub.Admin.Tests;

public sealed class RankCalculatorTests
{
    [Theory]
    [InlineData(995, 1000, "SSS")]
    [InlineData(199, 200, "SSS")]
    [InlineData(950, 1000, "S")]
    [InlineData(19, 20, "S")]
    [InlineData(900, 1000, "A")]
    [InlineData(9, 10, "A")]
    [InlineData(800, 1000, "B")]
    [InlineData(4, 5, "B")]
    [InlineData(700, 1000, "C")]
    [InlineData(7, 10, "C")]
    public void ExactlyAtTheThreshold_ReachesTheRank(int accepted, int total, string expected)
    {
        Assert.Equal(expected, RankCalculator.Calculate(accepted, total).Rank);
    }

    [Theory]
    [InlineData(9949, 10000, "S")]
    [InlineData(9499, 10000, "A")]
    [InlineData(8999, 10000, "B")]
    [InlineData(7999, 10000, "C")]
    [InlineData(6999, 10000, "D")]
    [InlineData(0, 10, "D")]
    public void JustBelowTheThreshold_FallsToTheRankUnder(int accepted, int total, string expected)
    {
        Assert.Equal(expected, RankCalculator.Calculate(accepted, total).Rank);
    }

    [Fact]
    public void PerfectDay_IsSss()
    {
        var result = RankCalculator.Calculate(42, 42);

        Assert.Equal(("SSS", 100m, "100%"), (result.Rank, result.AcceptedPercent, result.PercentText));
    }

    [Fact]
    public void NoValidations_ShowsADash()
    {
        var result = RankCalculator.Calculate(0, 0);

        Assert.Equal("—", result.Rank);
        Assert.False(result.HasData);
        Assert.Null(result.AcceptedPercent);
    }

    [Theory]
    [InlineData(9946, 10000, "99,4%", "S")]
    [InlineData(995, 1000, "99,5%", "SSS")]
    [InlineData(2, 3, "66,6%", "D")]
    public void Percent_IsTruncatedSoItNeverOverstatesTheRank(int accepted, int total, string text, string rank)
    {
        var result = RankCalculator.Calculate(accepted, total);

        Assert.Equal((text, rank), (result.PercentText, result.Rank));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1, -1)]
    [InlineData(11, 10)]
    public void RejectsImpossibleCounts(int accepted, int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RankCalculator.Calculate(accepted, total));
    }

    [Fact]
    public void Ladder_GoesFromDToSss()
    {
        Assert.Equal(["D", "C", "B", "A", "S", "SSS"], RankCalculator.Ladder);
    }
}
