using System.Net;
using Bunit;
using LicenseHub.Admin.Api;
using LicenseHub.Admin.Auth;
using LicenseHub.Admin.Components.Pages;
using LicenseHub.Admin.Dashboard;
using LicenseHub.Admin.Tests.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests.Components;

/// <summary>
/// The whole chain: a page calls the API through the real BearerTokenHandler; a 401 marks the circuit's token as
/// expired and sends the browser to /sessao-expirada, which clears the cookie (see AuthenticationTests).
/// </summary>
public sealed class SessionExpiryTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly TokenProvider _tokens = new();

    public SessionExpiryTests()
    {
        _tokens.Initialize("an-admin-jwt");
        var accessor = new CircuitServicesAccessor
        {
            Services = new ServiceCollection().AddSingleton(_tokens).BuildServiceProvider(),
        };
        var http = new HttpClient(new BearerTokenHandler(accessor) { InnerHandler = _api }) { BaseAddress = new Uri("http://api.test/") };

        Services.AddSingleton(new AdminApiClient(http));
        Services.AddSingleton(new DashboardClock(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"), TimeProvider.System));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/produtos")]
    public void ApiUnauthorized_EndsTheSession(string page)
    {
        _api.On(HttpMethod.Get, "/admin/stats/overview?tzOffsetMinutes=-180", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        _api.On(HttpMethod.Get, "/admin/validations?limit=10", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        _api.On(HttpMethod.Get, "/admin/products", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var navigation = Services.GetRequiredService<NavigationManager>();

        if (page == "/")
        {
            Render<Home>();
        }
        else
        {
            Render<Products>();
        }

        Assert.EndsWith($"/sessao-expirada?sessao={TokenProvider.SessionId("an-admin-jwt")}", navigation.Uri, StringComparison.Ordinal);
        Assert.True(_tokens.IsExpired);
        Assert.Null(_tokens.AccessToken);
        Assert.All(_api.Requests, r => Assert.Equal("an-admin-jwt", r.Headers.Authorization?.Parameter));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _api.Dispose();
        }
    }
}
