using System.Net;
using System.Net.Http.Json;
using LicenseHub.IntegrationTests.Infrastructure;

namespace LicenseHub.IntegrationTests;

public sealed class RateLimitTests(MariaDbFixture database) : ApiTestBase(database, licensesPermitLimit: 3)
{
    [Fact]
    public async Task Validate_Returns429WithRetryAfterOnceTheLimitIsUsed()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
        {
            last = await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01"));
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(last.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("rate_limited", await last.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_SharesTheStrictLimitButHealthIsNotLimited()
    {
        for (var i = 0; i < 3; i++)
        {
            await Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = "LH-0000-0000-0000-0000", instanceId = "x" }, Token);
        }

        var limited = await Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = "LH-0000-0000-0000-0000", instanceId = "x" }, Token);
        var health = await Client.GetAsync("/health", Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
