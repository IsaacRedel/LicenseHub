using System.Globalization;

namespace LicenseHub.Admin.Dashboard;

/// <param name="Rank">"SSS", "S", "A", "B", "C", "D", or <see cref="RankCalculator.NoData"/>.</param>
/// <param name="AcceptedPercent">Accepted share truncated to one decimal (99.46% is 99.4), so the number never
/// looks like it reached a threshold the rank did not; null without validations.</param>
public sealed record RankResult(string Rank, decimal? AcceptedPercent)
{
    public bool HasData => AcceptedPercent is not null;

    /// <summary>The percentage as shown in the card, e.g. "99,5%".</summary>
    public string PercentText => AcceptedPercent is { } percent
        ? percent.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + "%"
        : RankCalculator.NoData;
}

/// <summary>Rank of the day (DESIGN.md): the share of today's validations that were accepted.</summary>
public static class RankCalculator
{
    public const string NoData = "—";

    /// <summary>Minimum accepted share per rank, in tenths of a percent (995 = 99.5%), best first. Below C is D.</summary>
    public static readonly IReadOnlyList<(string Rank, int MinPerMille)> Thresholds =
    [
        ("SSS", 995),
        ("S", 950),
        ("A", 900),
        ("B", 800),
        ("C", 700),
    ];

    /// <summary>The ladder shown under the letter, lowest first.</summary>
    public static readonly IReadOnlyList<string> Ladder = ["D", "C", "B", "A", "S", "SSS"];

    public static RankResult Calculate(int accepted, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accepted);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(accepted, total);

        if (total == 0)
        {
            return new RankResult(NoData, null);
        }

        // Integer comparison: accepted / total >= min / 1000, with no floating point at the exact thresholds.
        var rank = Thresholds.FirstOrDefault(t => (long)accepted * 1000 >= (long)t.MinPerMille * total).Rank ?? "D";
        var percent = Math.Floor((decimal)accepted * 1000 / total) / 10;

        return new RankResult(rank, percent);
    }
}
