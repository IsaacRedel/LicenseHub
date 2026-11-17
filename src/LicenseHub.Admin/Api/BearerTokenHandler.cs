using System.Net;
using System.Net.Http.Headers;
using LicenseHub.Admin.Auth;

namespace LicenseHub.Admin.Api;

/// <summary>The API rejected the admin token (expired, revoked admin or rotated key): the panel session must end.</summary>
public sealed class SessionExpiredException() : Exception("The admin session expired.");

/// <summary>
/// Adds "Authorization: Bearer" to every call of <see cref="AdminApiClient"/> with the token of the current circuit,
/// and turns a 401 from the API into <see cref="SessionExpiredException"/>.
/// </summary>
public sealed class BearerTokenHandler(CircuitServicesAccessor circuitServices) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokens = circuitServices.Services?.GetService<TokenProvider>();
        if (tokens?.AccessToken is not { } token)
        {
            throw new SessionExpiredException();
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            tokens.MarkExpired();
            throw new SessionExpiredException();
        }

        return response;
    }
}
