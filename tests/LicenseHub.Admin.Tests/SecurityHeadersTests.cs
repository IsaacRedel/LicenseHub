using LicenseHub.Admin.Security;
using LicenseHub.Admin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LicenseHub.Admin.Tests;

public sealed class SecurityHeadersTests : IDisposable
{
    private readonly AdminPanelFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/app.css")]
    [InlineData("/pagina-que-nao-existe")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string path)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("camera=(), microphone=(), geolocation=()", Header(response, "Permissions-Policy"));
    }

    [Fact]
    public void Policy_IsTheAgreedOne()
    {
        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
            "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'",
            SecurityHeaders.ContentSecurityPolicy);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
