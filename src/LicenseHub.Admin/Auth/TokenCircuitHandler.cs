using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LicenseHub.Admin.Auth;

/// <summary>
/// Captures the admin JWT once, when the circuit opens. The circuit's user comes from the cookie sent with the
/// Blazor connection, so no IHttpContextAccessor is needed.
/// </summary>
internal sealed class TokenCircuitHandler(AuthenticationStateProvider authenticationState, TokenProvider tokens)
    : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        tokens.Initialize(state.User.FindFirst(AdminClaims.AccessToken)?.Value);
    }
}
