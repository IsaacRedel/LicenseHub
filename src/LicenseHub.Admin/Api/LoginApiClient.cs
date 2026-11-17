using System.Net;
using System.Net.Http.Json;

namespace LicenseHub.Admin.Api;

public enum LoginOutcome
{
    Success = 1,
    InvalidCredentials = 2,

    /// <summary>The API rate limit answered 429.</summary>
    TooManyAttempts = 3,
    ApiUnavailable = 4,
}

public sealed record LoginResult(LoginOutcome Outcome, string? AccessToken = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Calls POST /admin/auth/login from the server. Not authenticated, so it does not go through
/// <see cref="BearerTokenHandler"/>. The browser's address is forwarded so the API rate limit applies per person,
/// not to the panel as a whole (the API trusts the header only from the panel's address).
/// </summary>
public sealed partial class LoginApiClient(HttpClient http, ILogger<LoginApiClient> logger)
{
    public async Task<LoginResult> LoginAsync(string email, string password, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "admin/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };

        if (clientIp is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp.ToString());
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            LogApiUnreachable(ex.Message);
            return new LoginResult(LoginOutcome.ApiUnavailable);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
                    return body is { AccessToken.Length: > 0 }
                        ? new LoginResult(LoginOutcome.Success, body.AccessToken, body.ExpiresAt)
                        : new LoginResult(LoginOutcome.ApiUnavailable);
                case HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest:
                    return new LoginResult(LoginOutcome.InvalidCredentials);
                case HttpStatusCode.TooManyRequests:
                    return new LoginResult(LoginOutcome.TooManyAttempts);
                default:
                    LogUnexpectedStatus((int)response.StatusCode);
                    return new LoginResult(LoginOutcome.ApiUnavailable);
            }
        }
    }

    private sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin login: the API is unreachable ({Reason}).")]
    private partial void LogApiUnreachable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin login: unexpected API status {Status}.")]
    private partial void LogUnexpectedStatus(int status);
}
