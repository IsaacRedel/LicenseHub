using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.Domain.Validation;
using LicenseHub.IntegrationTests.Infrastructure;

namespace LicenseHub.IntegrationTests;

public sealed class AdminOverviewTests(MariaDbFixture database) : ApiTestBase(database)
{
    private const int SaoPauloOffset = -180;

    [Fact]
    public async Task Overview_AggregatesKpisTodayThirtyDaysAndProductUsage()
    {
        using var admin = await CreateAdminClientAsync();
        var busy = await IssueLicenseAsync(maxActivations: 2);
        var revoked = await IssueLicenseAsync(maxActivations: 3);
        await admin.PostAsJsonAsync($"/admin/licenses/{revoked.Id}/revoke", new { reason = "Chargeback" }, Token);

        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-02"));
        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-03"));
        await PostValidateAsync(NewRequest(revoked.Key, revoked.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest("LH-0000-0000-0000-0000", busy.ProductSlug, "srv-01"));

        var before = LocalToday(SaoPauloOffset);
        var overview = await JsonAsync(await admin.GetAsync($"/admin/stats/overview?tzOffsetMinutes={SaoPauloOffset}", Token));
        var after = LocalToday(SaoPauloOffset);

        var kpis = overview.GetProperty("kpis");
        Assert.Equal(
            (1, 1, 0, 5),
            (Int(kpis, "activeLicenses"), Int(kpis, "revokedLicenses"), Int(kpis, "expiredLicenses"), Int(kpis, "validationsToday")));

        var today = overview.GetProperty("today");
        Assert.Contains(DateOnly.Parse(today.GetProperty("date").GetString()!, CultureInfo.InvariantCulture), new[] { before, after });
        Assert.Equal(
            (5, 2, 1, 1, 1, 0, 0),
            (Int(today, "total"), Int(today, "accepted"), Int(today, "activationLimitReached"), Int(today, "revoked"),
             Int(today, "unknownKey"), Int(today, "expired"), Int(today, "productMismatch")));

        var days = overview.GetProperty("last30Days").EnumerateArray().ToList();
        Assert.Equal(30, days.Count);
        Assert.Equal((2, 3), (days.Sum(d => Int(d, "accepted")), days.Sum(d => Int(d, "refused"))));

        var products = overview.GetProperty("products").EnumerateArray().ToList();
        var busyUsage = products.Single(p => p.GetProperty("slug").GetString() == busy.ProductSlug);
        var revokedUsage = products.Single(p => p.GetProperty("slug").GetString() == revoked.ProductSlug);
        Assert.Equal((1, 2, 2), (Int(busyUsage, "activeLicenses"), Int(busyUsage, "activationsUsed"), Int(busyUsage, "activationCapacity")));
        Assert.Equal((0, 0, 0), (Int(revokedUsage, "activeLicenses"), Int(revokedUsage, "activationsUsed"), Int(revokedUsage, "activationCapacity")));
        Assert.Equal(busy.ProductSlug, products[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Overview_GroupsDaysByTheRequestedOffset()
    {
        using var admin = await CreateAdminClientAsync();
        var license = await IssueLicenseAsync(maxActivations: 1);

        // One minute before local midnight at UTC-3: yesterday there, but today in UTC between 00:00 and 03:00.
        var localToday = LocalToday(SaoPauloOffset);
        var lastMinuteOfYesterday = new DateTimeOffset(localToday.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(SaoPauloOffset))
            .AddMinutes(-1);
        await QueryAsync(async db =>
        {
            db.ValidationLogs.Add(ValidationLog.Create(license.Id, ValidationOutcome.Valid, "203.0.113.7", lastMinuteOfYesterday));
            return await db.SaveChangesAsync(Token);
        });

        var overview = await JsonAsync(await admin.GetAsync($"/admin/stats/overview?tzOffsetMinutes={SaoPauloOffset}", Token));
        var days = overview.GetProperty("last30Days").EnumerateArray().ToList();

        var yesterday = days.Single(d => d.GetProperty("date").GetString() == localToday.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(1, Int(yesterday, "accepted"));
        Assert.Equal(0, Int(overview.GetProperty("today"), "accepted"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("841")]
    [InlineData("-900")]
    public async Task Overview_RejectsInvalidOffsets(string offset)
    {
        using var admin = await CreateAdminClientAsync();

        var response = await admin.GetAsync($"/admin/stats/overview?tzOffsetMinutes={offset}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Validations_ListTheMostRecentFirstAndFilterByLicense()
    {
        using var admin = await CreateAdminClientAsync();
        var first = await IssueLicenseAsync(maxActivations: 5);
        var second = await IssueLicenseAsync(maxActivations: 5);
        for (var i = 0; i < 12; i++)
        {
            await PostValidateAsync(NewRequest(first.Key, first.ProductSlug, $"srv-{i:00}"));
        }

        await PostValidateAsync(NewRequest(second.Key, second.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest("LH-0000-0000-0000-0000", first.ProductSlug, "srv-01"));

        var latestBody = await admin.GetStringAsync("/admin/validations", Token);
        var latest = JsonDocument.Parse(latestBody).RootElement.EnumerateArray().ToList();
        var forSecond = (await JsonAsync(await admin.GetAsync($"/admin/validations?licenseId={second.Id}&limit=50", Token))).EnumerateArray().ToList();

        Assert.Equal(10, latest.Count);
        Assert.Equal("unknownKey", latest[0].GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, latest[0].GetProperty("productSlug").ValueKind);
        Assert.Equal(second.ProductSlug, latest[1].GetProperty("productSlug").GetString());
        var times = latest.Select(v => v.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.DoesNotContain(first.Key, latestBody, StringComparison.OrdinalIgnoreCase);

        var only = Assert.Single(forSecond);
        Assert.Equal(second.Id, only.GetProperty("licenseId").GetGuid());
        Assert.Equal("valid", only.GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("licenseId=not-a-guid")]
    public async Task Validations_RejectInvalidParameters(string query)
    {
        using var admin = await CreateAdminClientAsync();

        var response = await admin.GetAsync($"/admin/validations?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static DateOnly LocalToday(int offsetMinutes) =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(offsetMinutes)).DateTime);

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
    }
}
