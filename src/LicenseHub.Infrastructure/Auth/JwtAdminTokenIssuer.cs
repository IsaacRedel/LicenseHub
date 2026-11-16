using System.Security.Claims;
using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Admins;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LicenseHub.Infrastructure.Auth;

/// <summary>Issues short-lived (30 minute) HS256 access tokens. There are no refresh tokens.</summary>
internal sealed class JwtAdminTokenIssuer(AdminJwtSettings settings) : IAdminTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AdminToken Issue(AdminUser admin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var expiresAt = now + AdminJwtSettings.TokenLifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, admin.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256),
        });

        return new AdminToken(token, expiresAt);
    }
}
