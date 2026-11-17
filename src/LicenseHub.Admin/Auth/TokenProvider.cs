using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LicenseHub.Admin.Auth;

/// <summary>
/// Circuit-scoped holder of the admin JWT. Filled once when the circuit opens (<see cref="TokenCircuitHandler"/>)
/// from the authenticated cookie principal; read by <see cref="Api.BearerTokenHandler"/> for every API call.
/// </summary>
public sealed class TokenProvider
{
    public string? AccessToken { get; private set; }

    /// <summary>
    /// The panel session this circuit belongs to (<see cref="SessionId"/> of its token). Kept after the token
    /// expires, so /sessao-expirada can tell whether the cookie still holds that same session.
    /// </summary>
    public string? Session { get; private set; }

    /// <summary>Set when the API answered 401 or the session was logged out: the session must end.</summary>
    public bool IsExpired { get; private set; }

    public void Initialize(string? accessToken)
    {
        AccessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
        Session = AccessToken is null ? null : SessionId(AccessToken);
        IsExpired = false;
    }

    public void MarkExpired()
    {
        AccessToken = null;
        IsExpired = true;
    }

    /// <summary>
    /// A short, non-reversible id of a session: the first 16 base64url characters of the token's SHA-256. Safe to
    /// put in a URL; the token itself never leaves the server.
    /// </summary>
    public static string SessionId(string accessToken) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)))[..16];
}
