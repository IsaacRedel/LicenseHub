using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseHub.Domain.Licenses;
using LicenseHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.IntegrationTests;

public sealed class AdminLicenseTests(MariaDbFixture database) : ApiTestBase(database)
{
    [Fact]
    public async Task Products_CanBeCreatedReadAndPatched()
    {
        using var admin = await CreateAdminClientAsync();

        var created = await admin.PostAsJsonAsync("/admin/products", new { name = "Garage", slug = "garage", defaultMaxActivations = 2 }, Token);
        var duplicate = await admin.PostAsJsonAsync("/admin/products", new { name = "Other", slug = "GARAGE", defaultMaxActivations = 2 }, Token);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var patched = await admin.PatchAsJsonAsync($"/admin/products/{id}", new { name = "Garage Pro", defaultMaxActivations = 4 }, Token);
        var missing = await admin.GetAsync($"/admin/products/{Guid.CreateVersion7()}", Token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/admin/products/{id}", created.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var product = await JsonAsync(patched);
        Assert.Equal(("Garage Pro", 4, "garage"), (product.GetProperty("name").GetString(), product.GetProperty("defaultMaxActivations").GetInt32(), product.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Key_AppearsOnlyInTheIssueResponse()
    {
        using var admin = await CreateAdminClientAsync();
        var productId = await CreateProductAsync(admin, "keyed");

        var issued = await admin.PostAsJsonAsync("/admin/licenses", new { productId, customerEmail = "buyer@example.com" }, Token);
        var issuedJson = await JsonAsync(issued);
        var key = issuedJson.GetProperty("key").GetString()!;
        var id = issuedJson.GetProperty("id").GetGuid();
        var fullHash = Convert.ToHexString(await QueryAsync(db => db.Licenses.Where(l => l.Id == id).Select(l => l.KeyHash).SingleAsync(Token)));

        var details = await admin.GetStringAsync($"/admin/licenses/{id}", Token);
        var list = await admin.GetStringAsync("/admin/licenses", Token);

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Matches("^LH(-[0-9A-Z]{4}){4}$", key);
        foreach (var body in new[] { details, list })
        {
            Assert.DoesNotContain(key, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fullHash, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"key\"", body, StringComparison.Ordinal);
        }

        Assert.Equal(fullHash[..8], JsonDocument.Parse(details).RootElement.GetProperty("keyHashPrefix").GetString());
    }

    [Fact]
    public async Task Revoke_IsIdempotentAndMakesValidateReturnRevoked()
    {
        using var admin = await CreateAdminClientAsync();
        var productId = await CreateProductAsync(admin, "revocable");
        var issued = await JsonAsync(await admin.PostAsJsonAsync("/admin/licenses", new { productId, customerEmail = "buyer@example.com" }, Token));
        var id = issued.GetProperty("id").GetGuid();
        var key = issued.GetProperty("key").GetString()!;
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(key, "revocable", "srv-01")))).Valid);

        var first = await JsonAsync(await admin.PostAsJsonAsync($"/admin/licenses/{id}/revoke", new { reason = "Chargeback" }, Token));
        var second = await JsonAsync(await admin.PostAsJsonAsync($"/admin/licenses/{id}/revoke", new { reason = "Duplicate" }, Token));
        var verdict = await ReadVerifiedAsync(await PostValidateAsync(NewRequest(key, "revocable", "srv-01")));

        Assert.Equal("revoked", first.GetProperty("status").GetString());
        var revocation = first.GetProperty("revocation");
        Assert.Equal("Chargeback", revocation.GetProperty("reason").GetString());
        Assert.EndsWith("@example.com", revocation.GetProperty("revokedBy").GetProperty("email").GetString(), StringComparison.Ordinal);
        Assert.Equal(revocation.GetRawText(), second.GetProperty("revocation").GetRawText());
        Assert.Equal(("revoked", false), (verdict.Reason, verdict.Valid));
    }

    [Fact]
    public async Task ReleaseActivation_FreesASlotForAnotherInstance()
    {
        using var admin = await CreateAdminClientAsync();
        var license = await IssueLicenseAsync(maxActivations: 1);
        await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-01"));
        Assert.Equal("activation_limit_reached", (await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-02")))).Reason);

        var released = await admin.DeleteAsync($"/admin/licenses/{license.Id}/activations/srv-01", Token);
        var again = await admin.DeleteAsync($"/admin/licenses/{license.Id}/activations/srv-01", Token);

        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "srv-02")))).Valid);
    }

    [Theory]
    [InlineData("host%2Fvm1")]
    [InlineData("host/vm1")]
    public async Task ReleaseActivation_AcceptsInstanceIdsWithSlashes(string pathSegment)
    {
        using var admin = await CreateAdminClientAsync();
        var license = await IssueLicenseAsync(maxActivations: 1);
        Assert.True((await ReadVerifiedAsync(await PostValidateAsync(NewRequest(license.Key, license.ProductSlug, "host/vm1")))).Valid);

        var released = await admin.DeleteAsync($"/admin/licenses/{license.Id}/activations/{pathSegment}", Token);

        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        Assert.Equal(0, await QueryAsync(db => db.Activations.CountAsync(a => a.LicenseId == license.Id, Token)));
    }

    [Fact]
    public async Task List_PagesInAStableOrderAndFiltersByEffectiveStatusAndProduct()
    {
        using var admin = await CreateAdminClientAsync();
        var productA = await CreateProductAsync(admin, "product-a");
        var productB = await CreateProductAsync(admin, "product-b");
        var idsA = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            idsA.Add((await JsonAsync(await admin.PostAsJsonAsync("/admin/licenses", new { productId = productA, customerEmail = $"a{i}@example.com" }, Token))).GetProperty("id").GetGuid());
        }

        for (var i = 0; i < 2; i++)
        {
            await admin.PostAsJsonAsync("/admin/licenses", new { productId = productB, customerEmail = $"b{i}@example.com" }, Token);
        }

        await admin.PostAsJsonAsync($"/admin/licenses/{idsA[0]}/revoke", new { reason = "Chargeback" }, Token);
        await QueryAsync(db => db.Licenses.Where(l => l.Id == idsA[1])
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)), Token));

        var pages = new List<JsonElement>();
        for (var page = 1; page <= 3; page++)
        {
            pages.Add(await JsonAsync(await admin.GetAsync($"/admin/licenses?productSlug=PRODUCT-A&page={page}&pageSize=2", Token)));
        }

        var pagedIds = pages.SelectMany(p => p.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())).ToList();
        Assert.All(pages, p => Assert.Equal((5, 3, 2), (p.GetProperty("totalItems").GetInt32(), p.GetProperty("totalPages").GetInt32(), p.GetProperty("pageSize").GetInt32())));
        Assert.Equal(Enumerable.Reverse(idsA), pagedIds);

        Assert.Equal([idsA[0]], await IdsAsync(admin, "status=revoked&productSlug=product-a"));
        Assert.Equal([idsA[1]], await IdsAsync(admin, "status=expired"));
        Assert.Equal(idsA.Skip(2).Reverse(), await IdsAsync(admin, "status=active&productSlug=product-a"));
        Assert.Equal(7, (await JsonAsync(await admin.GetAsync("/admin/licenses", Token))).GetProperty("totalItems").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/licenses?pageSize=101", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/licenses?status=deleted", Token)).StatusCode);
    }

    [Fact]
    public async Task Stats_CountLicensesAndTodaysValidations()
    {
        using var admin = await CreateAdminClientAsync();
        var busy = await IssueLicenseAsync(maxActivations: 1);
        var revoked = await IssueLicenseAsync(maxActivations: 1);
        await admin.PostAsJsonAsync($"/admin/licenses/{revoked.Id}/revoke", new { reason = "Chargeback" }, Token);

        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest(busy.Key, busy.ProductSlug, "srv-02"));
        await PostValidateAsync(NewRequest(revoked.Key, revoked.ProductSlug, "srv-01"));
        await PostValidateAsync(NewRequest("LH-0000-0000-0000-0000", busy.ProductSlug, "srv-01"));

        var dayBefore = DateOnly.FromDateTime(DateTime.UtcNow);
        var stats = await JsonAsync(await admin.GetAsync("/admin/stats", Token));
        var dayAfter = DateOnly.FromDateTime(DateTime.UtcNow);

        var byStatus = stats.GetProperty("licensesByStatus");
        Assert.Equal((1, 1, 0), (byStatus.GetProperty("active").GetInt32(), byStatus.GetProperty("revoked").GetInt32(), byStatus.GetProperty("expired").GetInt32()));

        // Totals over the period instead of "today": the test may run across midnight UTC.
        var days = stats.GetProperty("validationsPerDay").EnumerateArray().ToList();
        Assert.Equal(30, days.Count);
        var lastDay = DateOnly.Parse(days[^1].GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(lastDay, new[] { dayBefore, dayAfter });
        int Total(string outcome) => days.Sum(d => d.GetProperty(outcome).GetInt32());
        Assert.Equal(
            (2, 1, 1, 1, 0, 0),
            (Total("valid"), Total("activationLimitReached"), Total("revoked"), Total("unknownKey"), Total("expired"), Total("productMismatch")));
        Assert.All(days.SkipLast(2), d => Assert.Equal(0, d.GetProperty("valid").GetInt32()));

        var top = stats.GetProperty("topProducts").EnumerateArray().ToList();
        Assert.Equal((busy.ProductSlug, 3), (top[0].GetProperty("slug").GetString(), top[0].GetProperty("validations").GetInt32()));
        Assert.Equal((revoked.ProductSlug, 1), (top[1].GetProperty("slug").GetString(), top[1].GetProperty("validations").GetInt32()));
    }

    private static async Task<Guid> CreateProductAsync(HttpClient admin, string slug)
    {
        var response = await admin.PostAsJsonAsync("/admin/products", new { name = slug, slug, defaultMaxActivations = 2 }, Token);
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient admin, string query) =>
        (await JsonAsync(await admin.GetAsync($"/admin/licenses?{query}", Token)))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
}
