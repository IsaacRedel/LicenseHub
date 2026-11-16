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
