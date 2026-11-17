using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LicenseHub.Admin.Auth;

/// <summary>
/// Captures the admin JWT once, when the circuit opens, and registers the circuit with its session so a logout in
/// any tab reaches it. The circuit's user comes from the cookie sent with the Blazor connection, so no
/// IHttpContextAccessor is needed.
/// </summary>
internal sealed class TokenCircuitHandler(
    AuthenticationStateProvider authenticationState,
    TokenProvider tokens,
    SessionRegistry sessions) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        tokens.Initialize(state.User.FindFirst(AdminClaims.AccessToken)?.Value);

        if (tokens.Session is { } session && !sessions.Register(session, tokens))
        {
            tokens.MarkExpired();
        }
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (tokens.Session is { } session)
        {
            sessions.Unregister(session, tokens);
        }

        return Task.CompletedTask;
    }
}
