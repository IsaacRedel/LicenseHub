using Bunit;
using LicenseHub.Admin.Api;
using LicenseHub.Admin.Components.Dashboard;
using LicenseHub.Admin.Components.Shared;
using LicenseHub.Admin.Dashboard;

namespace LicenseHub.Admin.Tests.Components;

public sealed class DashboardComponentTests : BunitContext
{
    [Theory]
    [InlineData(199, 200, "SSS", "99,5%")]
    [InlineData(95, 100, "S", "95%")]
    [InlineData(9, 10, "A", "90%")]
    [InlineData(15, 19, "C", "78,9%")]
    [InlineData(1, 10, "D", "10%")]
    public void RankCard_ShowsTheLetterAndTheAcceptedShare(int accepted, int total, string letter, string percent)
    {
        var card = Render<RankCard>(p => p.Add(c => c.Result, RankCalculator.Calculate(accepted, total)));

        Assert.Equal(letter, card.Find(".rank__letter").TextContent);
        Assert.Contains($"{percent} das validações foram aceitas hoje", card.Find(".rank__text").TextContent, StringComparison.Ordinal);
        var current = card.Find(".rank__step--on");
        Assert.Equal(letter, current.TextContent);
        Assert.Equal("true", current.GetAttribute("aria-current"));
    }

    [Fact]
    public void RankCard_WithoutValidations_ShowsDashAndMessage()
    {
        var card = Render<RankCard>(p => p.Add(c => c.Result, RankCalculator.Calculate(0, 0)));

        Assert.Equal("—", card.Find(".rank__letter").TextContent);
        Assert.Equal("Nenhuma validação hoje", card.Find(".rank__text").TextContent.Trim());
        Assert.Empty(card.FindAll(".rank__step--on"));
    }

    [Theory]
    [InlineData(true, "VÁLIDA", "■", "status--ok")]
    [InlineData(false, "CHAVE INEXISTENTE", "▲", "status--problem")]
    [InlineData(false, "REVOGADA", "▲", "status--problem")]
    public void StatusBadge_AlwaysPairsSymbolAndText(bool ok, string text, string symbol, string cssClass)
    {
        var badge = Render<StatusBadge>(p => p.Add(b => b.Ok, ok).Add(b => b.Text, text));

        var status = badge.Find(".status");
        Assert.Contains(cssClass, status.ClassName, StringComparison.Ordinal);
        Assert.Equal(symbol, badge.Find(".status__symbol").TextContent);
        Assert.Equal("true", badge.Find(".status__symbol").GetAttribute("aria-hidden"));
        Assert.Equal($"{symbol} {text}", status.TextContent.Trim());
    }

    [Fact]
    public void ValidationChart_DrawsStackedBarsWithSvgAttributesAndAnAccessibleTable()
    {
        var days = Enumerable.Range(0, 30)
            .Select(i => new DayValidations(new DateOnly(2026, 10, 19).AddDays(i), Accepted: i == 29 ? 30 : 0, Refused: i == 29 ? 10 : 0))
            .ToList();

        var chart = Render<ValidationChart>(p => p.Add(c => c.Days, days));

        var accepted = Assert.Single(chart.FindAll(".chart__accepted"));
        var refused = Assert.Single(chart.FindAll(".chart__refused"));
        Assert.Equal("150", accepted.GetAttribute("height"));
        Assert.Equal("50", refused.GetAttribute("height"));
        Assert.Empty(chart.FindAll("[style]"));
        Assert.Contains("30 aceitas e 10 recusadas", chart.Find("figure").GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Equal("true", chart.Find("svg").GetAttribute("aria-hidden"));
        Assert.Equal(30, chart.FindAll(".visually-hidden table tbody tr").Count);
        Assert.Equal(6, chart.FindAll("text").Count(t => t.TextContent.Contains('/')));
    }

    [Fact]
    public void ProgressBar_UsesSvgWidthNotInlineStyle()
    {
        var bar = Render<ProgressBar>(p => p.Add(b => b.Value, 8).Add(b => b.Max, 14).Add(b => b.Height, 3));

        Assert.Equal("57.14%", bar.Find(".progress__fill").GetAttribute("width"));
        Assert.Equal("3", bar.Find("svg").GetAttribute("height"));
        Assert.Empty(bar.FindAll("[style]"));
    }
}
