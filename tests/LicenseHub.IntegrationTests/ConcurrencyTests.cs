using System.Net;
using System.Net.Http.Json;
using LicenseHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.IntegrationTests;

public sealed class ConcurrencyTests(MariaDbFixture database) : ApiTestBase(database)
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(3, 12)]
    public async Task ParallelNewInstances_NeverExceedMaxActivations(int maxActivations, int instances)
    {
        var license = await IssueLicenseAsync(maxActivations);

        var responses = await Task.WhenAll(Enumerable.Range(1, instances).Select(i =>
            PostValidateAsync(NewRequest(license.Key, license.ProductSlug, $"srv-{i:00}"))));
        var payloads = await Task.WhenAll(responses.Select(ReadVerifiedAsync));

        Assert.Equal(maxActivations, payloads.Count(p => p.Valid));
        Assert.All(payloads.Where(p => !p.Valid), p => Assert.Equal("activation_limit_reached", p.Reason));

        var stored = await QueryAsync(db => db.Activations.CountAsync(a => a.LicenseId == license.Id, Token));
        Assert.Equal(maxActivations, stored);
    }

    [Fact]
    public async Task ParallelDeactivations_RemoveOnceAndAnswerNotFoundToTheRest()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01")))).Valid);

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(_ =>
            Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = license.Key, instanceId = "srv-01" }, Token)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(0, await QueryAsync(db => db.Activations.CountAsync(a => a.LicenseId == license.Id, Token)));
    }

    [Fact]
    public async Task InterleavedValidateAndDeactivate_NeverFailAndLogEveryValidation()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);

        var requests = Enumerable.Range(0, 16).Select(i => i % 2 == 0
            ? PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01"))
            : Client.PostAsJsonAsync("/v1/licenses/deactivate", new { key = license.Key, instanceId = "srv-01" }, Token));
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"Unexpected {(int)r.StatusCode}"));
        Assert.Equal(8, await QueryAsync(db => db.ValidationLogs.CountAsync(l => l.LicenseId == license.Id, Token)));
    }

    [Fact]
    public async Task ParallelRequestsFromTheSameInstance_CreateOneActivation()
    {
        var license = await IssueLicenseAsync(maxActivations: 1);

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(_ =>
            PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01"))));
        var payloads = await Task.WhenAll(responses.Select(ReadVerifiedAsync));

        Assert.All(payloads, p => Assert.True(p.Valid));
        Assert.Equal(1, await QueryAsync(db => db.Activations.CountAsync(a => a.LicenseId == license.Id, Token)));
    }
}
