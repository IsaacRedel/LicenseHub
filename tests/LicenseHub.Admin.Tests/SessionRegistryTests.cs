using System.Security.Claims;
using LicenseHub.Admin.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.Admin.Tests;

public sealed class SessionRegistryTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionRegistry _sessions;

    public SessionRegistryTests() => _sessions = new SessionRegistry(_time);

    [Fact]
    public void End_DropsTheTokenOfEveryTabOfThatSessionOnly()
    {
        var tabA = Circuit("token-1");
        var tabB = Circuit("token-1");
        var otherAdmin = Circuit("token-2");

        _sessions.End(TokenProvider.SessionId("token-1"), _time.GetUtcNow().AddMinutes(30));

        Assert.True(tabA.IsExpired);
        Assert.True(tabB.IsExpired);
        Assert.Null(tabA.AccessToken);
        Assert.False(otherAdmin.IsExpired);
        Assert.Equal("token-2", otherAdmin.AccessToken);
    }

    [Fact]
    public void EndedSession_IsRefusedUntilItWouldExpire()
    {
        var session = TokenProvider.SessionId("token-1");
        _sessions.End(session, _time.GetUtcNow().AddMinutes(30));

        Assert.True(_sessions.IsEnded(session));
        Assert.False(_sessions.Register(session, new TokenProvider()));

        _time.Advance(TimeSpan.FromMinutes(30));
        Assert.False(_sessions.IsEnded(session));
    }

    [Fact]
    public void ClosedTab_IsNotTouchedByALaterLogout()
    {
        var tab = Circuit("token-1");
        _sessions.Unregister(tab.Session!, tab);

        _sessions.End(tab.Session!, _time.GetUtcNow().AddMinutes(30));

        Assert.False(tab.IsExpired);
    }

    [Fact]
    public async Task CircuitHandler_RegistersTheCircuitAndRefusesALoggedOutSession()
    {
        var open = new TokenProvider();
        await new TokenCircuitHandler(new FixedAuthenticationState("token-1"), open, _sessions)
            .OnCircuitOpenedAsync(null!, TestContext.Current.CancellationToken);
        Assert.Equal("token-1", open.AccessToken);

        _sessions.End(TokenProvider.SessionId("token-1"), _time.GetUtcNow().AddMinutes(30));
        Assert.True(open.IsExpired);

        var late = new TokenProvider();
        await new TokenCircuitHandler(new FixedAuthenticationState("token-1"), late, _sessions)
            .OnCircuitOpenedAsync(null!, TestContext.Current.CancellationToken);
        Assert.True(late.IsExpired);
        Assert.Null(late.AccessToken);
    }

    [Fact]
    public void SessionId_IsShortAndDoesNotContainTheToken()
    {
        var id = TokenProvider.SessionId("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature");

        Assert.Equal(16, id.Length);
        Assert.DoesNotContain("eyJ", id, StringComparison.Ordinal);
        Assert.Equal(id, TokenProvider.SessionId("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature"));
    }

    private TokenProvider Circuit(string token)
    {
        var tokens = new TokenProvider();
        tokens.Initialize(token);
        Assert.True(_sessions.Register(tokens.Session!, tokens));
        return tokens;
    }

    private sealed class FixedAuthenticationState(string token) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AdminClaims.AccessToken, token)], "test"))));
    }
}
