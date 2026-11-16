using System.Text;
using LicenseHub.Infrastructure.Signing;

namespace LicenseHub.Api.Commands;

/// <summary>
/// <c>keygen</c>: prints a new ECDSA P-256 key pair for signing validation responses.
/// Runs without configuration or database.
/// </summary>
internal static class KeygenCommand
{
    public const string Name = "keygen";

    public static int Run(TextWriter output)
    {
        using var key = ResponseSigningKey.Generate();
        var privateKeyBase64 = key.ExportPrivateKeyBase64();

        var text = new StringBuilder()
            .AppendLine("New response signing key (ECDSA P-256). Keep the private key secret and never commit it.")
            .AppendLine()
            .AppendLine($"keyId: {key.KeyId}")
            .AppendLine()
            .AppendLine("# docker compose: add this line to .env")
            .AppendLine($"SIGNING_PRIVATE_KEY={privateKeyBase64}")
            .AppendLine()
            .AppendLine("# Any environment: environment variable")
            .AppendLine($"Signing__PrivateKey={privateKeyBase64}")
            .AppendLine()
            .AppendLine("# Local dotnet run: user-secrets")
            .AppendLine($"dotnet user-secrets set \"Signing:PrivateKey\" \"{privateKeyBase64}\" --project src/LicenseHub.Api")
            .AppendLine()
            .AppendLine("# Public key for clients (also served by GET /v1/signing-key)")
            .AppendLine(key.PublicKeyPem);

        output.Write(text.ToString());
        return 0;
    }
}
