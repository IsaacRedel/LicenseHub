using System.Net;
using LicenseHub.IntegrationTests.Infrastructure;

namespace LicenseHub.IntegrationTests;

/// <summary>
/// Every request in these tests comes from the same in-memory client, like calls relayed by the admin panel:
/// authenticated admin routes must still be limited per admin, not per caller IP.
/// </summary>
public sealed class AdminRateLimitTests(MariaDbFixture database) : ApiTestBase(database)
{
    protected override void ConfigureSettings(Dictionary<string, string> settings) =>
        settings["RateLimiting:Default:PermitLimit"] = "8";

    [Fact]
    public async Task AdminRoutes_AreLimitedPerAdminNotPerIp()
    {
        // Logins and token setup also count against the shared IP partition, so create both admins first.
        using var first = await CreateAdminClientAsync();
        using var second = await CreateAdminClientAsync();

        var firstStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 9; i++)
        {
            firstStatuses.Add((await first.GetAsync("/admin/products", Token)).StatusCode);
        }

        var secondStatus = (await second.GetAsync("/admin/products", Token)).StatusCode;

        Assert.Equal(8, firstStatuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, firstStatuses[^1]);
        Assert.Equal(HttpStatusCode.OK, secondStatus);
    }
}
