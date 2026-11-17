using System.Security.Claims;
using LicenseHub.Admin.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

                // A logged-out session is refused even if its cookie is presented again (see SessionRegistry).
                options.Events.OnValidatePrincipal = async context =>
                {
                    var token = context.Principal?.FindFirst(AdminClaims.AccessToken)?.Value;
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionRegistry>();
                    if (token is null || sessions.IsEnded(TokenProvider.SessionId(token)))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionRegistry>();
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

    /// <summary>
    /// Where a page goes when its session ends: /sessao-expirada logs out only if the cookie still holds this
    /// circuit's session, so a 401 in an old tab does not end a newer login made in another tab.
    /// </summary>
    public static string SessionExpiredUrl(string? session) =>
        session is null ? SessionExpiredPath : $"{SessionExpiredPath}?sessao={Uri.EscapeDataString(session)}";

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
    /// or wrong token answers 400 instead of failing while the form is read. Ends the session in every tab.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminLogout(this IEndpointRouteBuilder app)
    {
        app.MapPost(LogoutPath, async (HttpContext httpContext, IAntiforgery antiforgery, SessionRegistry sessions, TimeProvider time) =>
            {
                if (!await antiforgery.IsRequestValidAsync(httpContext))
                {
                    return Results.BadRequest();
                }

                var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
                var current = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (current.Succeeded && current.Principal.FindFirst(AdminClaims.AccessToken)?.Value is { } token)
                {
                    sessions.End(TokenProvider.SessionId(token), current.Properties.ExpiresUtc ?? time.GetUtcNow().AddMinutes(30));
                }

                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect(form["reason"] == "expired" ? $"{LoginPath}?expirada=1" : LoginPath);
            })
            .DisableAntiforgery()
            .AllowAnonymous();

        return app;
    }
}
