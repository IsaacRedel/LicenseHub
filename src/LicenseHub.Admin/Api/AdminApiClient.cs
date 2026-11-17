namespace LicenseHub.Admin.Api;

/// <summary>
/// Typed client for the API's /admin routes. Every call goes through <see cref="BearerTokenHandler"/>, which adds
/// the admin token and throws <see cref="SessionExpiredException"/> on 401.
/// </summary>
public sealed partial class AdminApiClient(HttpClient http)
{
    internal HttpClient Http => http;
}
