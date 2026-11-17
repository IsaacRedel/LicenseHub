namespace LicenseHub.Admin.Auth;

/// <summary>Claims of the panel's cookie principal. The cookie ticket is encrypted by Data Protection.</summary>
public static class AdminClaims
{
    /// <summary>The API's admin JWT. Lives only in the encrypted cookie and in server memory, never in the browser.</summary>
    public const string AccessToken = "lh:access_token";

    public const string AdminId = "sub";
    public const string Email = "email";

    public const string CookieName = "lh-admin";
}
