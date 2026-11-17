using System.Security.Claims;
using LicenseHub.Admin.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;

using Microsoft.IdentityModel.JsonWebTokens;

namespace LicenseHub.Admin.Auth;

/// <summary>
/// Backend-for-frontend authentication: the panel exchanges the admin's credentials for the API's JWT on the server
/// and keeps it inside its own encrypted cookie. The browser never sees the JWT.
/// </summary>
public static class AdminAuthentication
{
    public const string LoginPath = "/login";
    public const string LogoutPath = "/logout";
    public const string SessionExpiredPath = "/sessao-expirada";

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Persisted keys: without them every restart would invalidate the cookies (and the antiforgery tokens).
        var dataProtection = services.AddDataProtection().SetApplicationName("LicenseHub.Admin");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = AdminClaims.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.LoginPath = LoginPath;
                options.LogoutPath = LogoutPath;
                options.AccessDeniedPath = LoginPath;

                // The session lasts exactly as long as the API token: no sliding renewal past its expiry.
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.AddScoped<TokenProvider>();
        services.AddSingleton<CircuitServicesAccessor>();
        services.AddScoped<CircuitHandler, ServicesAccessorCircuitHandler>();
        services.AddScoped<CircuitHandler, TokenCircuitHandler>();
        services.AddTransient<BearerTokenHandler>();

        var apiBaseUrl = configuration["LicenseHub:ApiBaseUrl"] is { Length: > 0 } url
            ? new Uri(url.EndsWith('/') ? url : url + "/")
            : throw new InvalidOperationException("LicenseHub:ApiBaseUrl is not configured.");

        services.AddHttpClient<LoginApiClient>(client => client.BaseAddress = apiBaseUrl);
        services.AddHttpClient<AdminApiClient>(client => client.BaseAddress = apiBaseUrl)
            .AddHttpMessageHandler<BearerTokenHandler>();

        return services;
    }

    /// <summary>Signs the admin in with a cookie that carries the API token and expires with it.</summary>
    public static Task SignInAsync(HttpContext httpContext, string accessToken, DateTimeOffset expiresAt)
    {
        var token = new JsonWebToken(accessToken);
        var identity = new ClaimsIdentity(
            [
                new Claim(AdminClaims.AdminId, token.Subject),
                new Claim(AdminClaims.Email, token.GetClaim(JwtRegisteredClaimNames.Email).Value),
                new Claim(AdminClaims.AccessToken, accessToken),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: AdminClaims.Email,
            roleType: null);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = expiresAt,
                AllowRefresh = false,
            });
    }

    /// <summary>
    /// POST /logout, protected by the antiforgery token of the form that posts it. Validated explicitly so a missing
    /// or wrong token answers 400 instead of failing while the form is read.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminLogout(this IEndpointRouteBuilder app)
    {
        app.MapPost(LogoutPath, async (HttpContext httpContext, IAntiforgery antiforgery) =>
            {
                if (!await antiforgery.IsRequestValidAsync(httpContext))
                {
                    return Results.BadRequest();
                }

                var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect(form["reason"] == "expired" ? $"{LoginPath}?expirada=1" : LoginPath);
            })
            .DisableAntiforgery()
            .AllowAnonymous();

        return app;
    }
}
