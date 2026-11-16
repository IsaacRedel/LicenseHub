using System.Text;
using LicenseHub.Infrastructure.Auth;
using LicenseHub.Infrastructure.Signing;

namespace LicenseHub.Api.Commands;

/// <summary>
/// <c>keygen</c>: prints a new ECDSA P-256 key pair for signing validation responses and a new HMAC key for
/// admin tokens. Runs without configuration or database.
/// </summary>
internal static class KeygenCommand
{
    public const string Name = "keygen";

    public static int Run(TextWriter output)
    {
        using var key = ResponseSigningKey.Generate();
        var privateKeyBase64 = key.ExportPrivateKeyBase64();
        var jwtKey = AdminJwtSettings.GenerateKey();

        var text = new StringBuilder()
            .AppendLine("New keys. Keep them secret and never commit them.")
            .AppendLine()
            .AppendLine("Response signing key (ECDSA P-256)")
            .AppendLine($"keyId: {key.KeyId}")
            .AppendLine()
            .AppendLine("Admin token key (HMAC-SHA256, 256 bits)")
            .AppendLine()
            .AppendLine("# docker compose: add these lines to .env")
            .AppendLine($"SIGNING_PRIVATE_KEY={privateKeyBase64}")
            .AppendLine($"ADMIN_JWT_KEY={jwtKey}")
            .AppendLine()
            .AppendLine("# Any environment: environment variables")
            .AppendLine($"Signing__PrivateKey={privateKeyBase64}")
            .AppendLine($"AdminAuth__JwtKey={jwtKey}")
            .AppendLine()
            .AppendLine("# Local dotnet run: user-secrets")
            .AppendLine($"dotnet user-secrets set \"Signing:PrivateKey\" \"{privateKeyBase64}\" --project src/LicenseHub.Api")
            .AppendLine($"dotnet user-secrets set \"AdminAuth:JwtKey\" \"{jwtKey}\" --project src/LicenseHub.Api")
            .AppendLine()
            .AppendLine("# Public key for clients (also served by GET /v1/signing-key)")
            .AppendLine(key.PublicKeyPem);

        output.Write(text.ToString());
        return 0;
    }
}
