using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LicenseHub.Domain.Validation;
using LicenseHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.IntegrationTests;

public sealed class ValidationFlowTests(MariaDbFixture database) : ApiTestBase(database)
{
    [Fact]
    public async Task FullFlow_ActivatesUpToTheLimitAndFreesASlotOnDeactivate()
    {
        var license = await IssueLicenseAsync(maxActivations: 2);

        var first = NewRequest(license.Key, license.ProductSlug, "srv-01");
        var firstPayload = await ReadVerifiedAsync(await PostValidateAsync(first));
        Assert.True(firstPayload.Valid);
        Assert.Null(firstPayload.Reason);
        Assert.Equal(license.Id, firstPayload.LicenseId);
        Assert.Equal(first.Nonce, firstPayload.Nonce);
        Assert.Equal("srv-01", firstPayload.InstanceId);
        Assert.InRange(firstPayload.IssuedAt, first.Timestamp!.Value - 5, first.Timestamp.Value + 60);

        // Same instance again: still valid, no extra activation.
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01")))).Valid);
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-02")))).Valid);

        var third = await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-03")));
        Assert.False(third.Valid);
        Assert.Equal("activation_limit_reached", third.Reason);
        Assert.Equal(license.Id, third.LicenseId);

        var deactivate = await Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = license.Key, instanceId = "srv-01" }, Token);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-03")))).Valid);

        var activeInstances = await QueryAsync(db => db.Activations
            .Where(a => a.LicenseId == license.Id)
            .Select(a => a.InstanceId)
            .OrderBy(id => id)
            .ToListAsync(Token));
        Assert.Equal(["srv-02", "srv-03"], activeInstances);

        var logs = await QueryAsync(db => db.ValidationLogs.Where(l => l.LicenseId == license.Id).ToListAsync(Token));
        Assert.Equal(5, logs.Count);
        Assert.Single(logs, l => l.Outcome == ValidationOutcome.ActivationLimitReached);
    }

    [Fact]
    public async Task Validate_ReturnsSignedRejections()
    {
        var revoked = await IssueLicenseAsync(maxActivations: 1);
        await RevokeAsync(revoked.Id);
        var other = await IssueLicenseAsync(maxActivations: 1);

        var unknown = await ReadVerifiedAsync(await PostValidateAsync(NewRequest("LH-0000-0000-0000-0000", other.ProductSlug, "srv-01")));
        var revokedPayload = await ReadVerifiedAsync(await PostValidateAsync(NewRequest(revoked.Key, revoked.ProductSlug, "srv-01")));
        var mismatch = await ReadVerifiedAsync(await PostValidateAsync(NewRequest(revoked.Key, other.ProductSlug, "srv-01")));

        Assert.Equal(("unknown_key", (Guid?)null), (unknown.Reason, unknown.LicenseId));
        Assert.Equal(("revoked", (Guid?)revoked.Id), (revokedPayload.Reason, revokedPayload.LicenseId));
        Assert.Equal(("product_mismatch", (Guid?)null), (mismatch.Reason, mismatch.LicenseId));
        Assert.False(unknown.Valid || revokedPayload.Valid || mismatch.Valid);
    }

    [Fact]
    public async Task Validate_ReturnsBadRequestForStaleTimestampAndMalformedBody()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);
        var stale = NewRequest(license.Key, license.ProductSlug, "srv-01") with
        {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds(),
        };

        var staleResponse = await PostValidateAsync(stale);
        var malformed = await Client.PostAsync(
            "/v1/licenses/validate",
            new StringContent("{\"key\":", Encoding.UTF8, "application/json"),
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync(Token));
        Assert.Equal("request.timestamp_out_of_window", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(0, await QueryAsync(db => db.ValidationLogs.CountAsync(l => l.LicenseId == license.Id, Token)));
    }

    [Fact]
    public async Task Deactivate_ReturnsNotFoundForUnknownKeyOrInstance()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);

        var unknownInstance = await Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = license.Key, instanceId = "never-seen" }, Token);
        var unknownKey = await Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = "LH-0000-0000-0000-0000", instanceId = "srv-01" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, unknownInstance.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownKey.StatusCode);
        Assert.Contains("activation.not_found", await unknownInstance.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Contains("activation.not_found", await unknownKey.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }
}
