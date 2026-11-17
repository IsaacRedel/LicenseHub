using System.Net;
using LicenseHub.Admin.Api;
using LicenseHub.Admin.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests;

public sealed class BearerTokenHandlerTests
{
    private readonly TokenProvider _tokens = new();
    private readonly CircuitServicesAccessor _accessor = new();

    public BearerTokenHandlerTests()
    {
        var services = new ServiceCollection().AddSingleton(_tokens).BuildServiceProvider();
        _accessor.Services = services;
    }

    [Fact]
    public async Task AddsTheCircuitTokenAsBearer()
    {
        _tokens.Initialize("the-admin-jwt");
        HttpRequestMessage? sent = null;
        using var client = Client(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.GetAsync("admin/products", TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", sent!.Headers.Authorization!.Scheme);
        Assert.Equal("the-admin-jwt", sent.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Unauthorized_EndsTheSession()
    {
        _tokens.Initialize("the-admin-jwt");
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetAsync("admin/products", TestContext.Current.CancellationToken));

        Assert.True(_tokens.IsExpired);
        Assert.Null(_tokens.AccessToken);
    }

    [Fact]
    public async Task WithoutAToken_DoesNotCallTheApi()
    {
        var called = false;
        using var client = Client(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAsync<SessionExpiredException>(() => client.GetAsync("admin/products", TestContext.Current.CancellationToken));

        Assert.False(called);
    }

    private HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new BearerTokenHandler(_accessor) { InnerHandler = new Stub(respond) }) { BaseAddress = new Uri("http://api.test/") };

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
