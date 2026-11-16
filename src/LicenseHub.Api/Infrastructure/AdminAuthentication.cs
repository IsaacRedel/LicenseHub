using System.Security.Claims;
using LicenseHub.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LicenseHub.Api.Infrastructure;

public static class AdminAuthentication
{
    /// <summary>Required by every /admin route except the login.</summary>
    public const string AdminPolicy = "admin";

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, AdminJwtSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the JWT claim names (sub, email) instead of the legacy SOAP claim URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = settings.CreateValidationParameters();
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(JwtRegisteredClaimNames.Sub));

        return services;
    }

    /// <summary>The id of the authenticated admin, from the token's sub claim.</summary>
    public static Guid GetAdminId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new InvalidOperationException("The admin token has no valid sub claim.");
    }
}
