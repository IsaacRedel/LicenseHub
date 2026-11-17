namespace LicenseHub.Admin.Auth;

/// <summary>
/// Circuit-scoped holder of the admin JWT. Filled once when the circuit opens (<see cref="TokenCircuitHandler"/>)
/// from the authenticated cookie principal; read by <see cref="Api.BearerTokenHandler"/> for every API call.
/// </summary>
public sealed class TokenProvider
{
    public string? AccessToken { get; private set; }

    /// <summary>Set when the API answered 401: the session must end.</summary>
    public bool IsExpired { get; private set; }

    public void Initialize(string? accessToken)
    {
        AccessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
        IsExpired = false;
    }

    public void MarkExpired()
    {
        AccessToken = null;
        IsExpired = true;
    }
}
