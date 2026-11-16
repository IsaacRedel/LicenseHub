using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LicenseHub.Domain.Admins;
using LicenseHub.Infrastructure.Auth;
using LicenseHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.IntegrationTests;

public sealed class AdminAuthTests(MariaDbFixture database) : ApiTestBase(database)
{
    [Fact]
    public async Task AdminRoutes_Return401WithoutAToken()
    {
        foreach (var path in new[] { "/admin/products", "/admin/licenses", "/admin/stats" })
        {
            var response = await Client.GetAsync(path, Token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("auth.unauthorized", await ProblemCodeAsync(response));
        }
    }

    [Fact]
    public async Task AdminRoutes_Return401ForAnExpiredToken()
    {
        var admin = AdminUser.Create("expired@example.com", "hash", DateTimeOffset.UtcNow);
        var settings = Factory.Services.GetRequiredService<AdminJwtSettings>();
        var issuedLongAgo = DateTimeOffset.UtcNow - AdminJwtSettings.TokenLifetime - AdminJwtSettings.ClockSkew - TimeSpan.FromMinutes(1);
        var token = new JwtAdminTokenIssuer(settings).Issue(admin, issuedLongAgo);

        var response = await GetWithTokenAsync("/admin/products", token.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminRoutes_Return401ForATokenSignedWithAnotherKey()
    {
        var admin = AdminUser.Create("forged@example.com", "hash", DateTimeOffset.UtcNow);
        var otherKey = AdminJwtSettings.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminAuth:JwtKey"] = AdminJwtSettings.GenerateKey(),
            }).Build(),
            allowEphemeralKey: false);
        var token = new JwtAdminTokenIssuer(otherKey).Issue(admin, DateTimeOffset.UtcNow);

        var response = await GetWithTokenAsync("/admin/products", token.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_IssuesATokenThatOpensAdminRoutes()
    {
        var email = await CreateAdminAsync();

        var login = await LoginAsync(email.ToUpperInvariant(), AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Token));
        Assert.Equal("Bearer", body.RootElement.GetProperty("tokenType").GetString());
        var expiresAt = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddMinutes(29), DateTimeOffset.UtcNow.AddMinutes(31));

        var products = await GetWithTokenAsync("/admin/products", body.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }

    [Fact]
    public async Task Login_AnswersTheSameErrorForWrongPasswordAndUnknownEmail()
    {
        var email = await CreateAdminAsync();

        var wrongPassword = await LoginAsync(email, "not the password at all");
        var unknownEmail = await LoginAsync("nobody@example.com", AdminPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await DetailAsync(wrongPassword), await DetailAsync(unknownEmail));
        Assert.Equal("auth.invalid_credentials", await ProblemCodeAsync(unknownEmail));
    }

    [Fact]
    public async Task Login_LocksTheAccountAfterFiveFailures()
    {
        var email = await CreateAdminAsync();
        for (var i = 0; i < AdminUser.MaxFailedLogins; i++)
        {
            await LoginAsync(email, "not the password at all");
        }

        var correctPassword = await LoginAsync(email, AdminPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
        Assert.Equal("auth.invalid_credentials", await ProblemCodeAsync(correctPassword));
        var admin = await QueryAsync(db => Task.FromResult(db.AdminUsers.Single(a => a.Email == email)));
        Assert.True(admin.IsLockedOut(DateTimeOffset.UtcNow));
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request, Token);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return problem.RootElement.GetProperty("detail").GetString();
    }
}

public sealed class AdminLoginRateLimitTests(MariaDbFixture database) : ApiTestBase(database)
{
    protected override void ConfigureSettings(Dictionary<string, string> settings) =>
        settings["RateLimiting:Login:PermitLimit"] = "3";

    [Fact]
    public async Task Login_Returns429AfterThePerIpLimit()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await LoginAsync("nobody@example.com", "whatever-password")).StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
    }
}
