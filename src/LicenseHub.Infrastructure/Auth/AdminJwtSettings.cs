using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LicenseHub.Infrastructure.Auth;

/// <summary>
/// Settings shared by the token issuer and the API's JWT bearer validation. The HMAC key is separate from the
/// ECDSA key that signs license responses.
/// </summary>
public sealed class AdminJwtSettings
{
    public const string KeySetting = "AdminAuth:JwtKey";
    public const int MinKeyBytes = 32;

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private AdminJwtSettings(byte[] key, string issuer, string audience, bool isEphemeral)
    {
        SigningKey = new SymmetricSecurityKey(key);
        Issuer = issuer;
        Audience = audience;
        IsEphemeral = isEphemeral;
    }

    public SymmetricSecurityKey SigningKey { get; }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>True when the key was generated at startup because none was configured (Development only).</summary>
    public bool IsEphemeral { get; }

    /// <summary>
    /// Reads <c>AdminAuth:JwtKey</c> (base64, at least 256 bits). When it is missing, an ephemeral key is used
    /// only if <paramref name="allowEphemeralKey"/> is true; otherwise startup fails.
    /// </summary>
    public static AdminJwtSettings FromConfiguration(IConfiguration configuration, bool allowEphemeralKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var issuer = configuration["AdminAuth:Issuer"] is { Length: > 0 } configuredIssuer ? configuredIssuer : "licensehub";
        var audience = configuration["AdminAuth:Audience"] is { Length: > 0 } configuredAudience ? configuredAudience : "licensehub-admin";
        var configuredKey = configuration[KeySetting];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (!allowEphemeralKey)
            {
                throw new InvalidOperationException(
                    $"'{KeySetting}' is not configured. Generate one with the 'keygen' command and set it with " +
                    "user-secrets or the AdminAuth__JwtKey environment variable.");
            }

            return new AdminJwtSettings(RandomNumberGenerator.GetBytes(MinKeyBytes), issuer, audience, isEphemeral: true);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"'{KeySetting}' must be base64.", ex);
        }

        if (key.Length < MinKeyBytes)
        {
            throw new InvalidOperationException($"'{KeySetting}' must decode to at least {MinKeyBytes} bytes (256 bits).");
        }

        return new AdminJwtSettings(key, issuer, audience, isEphemeral: false);
    }

    /// <summary>A new random key in the format <see cref="KeySetting"/> expects.</summary>
    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(MinKeyBytes));

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = ClockSkew,
        NameClaimType = "email",
    };
}
