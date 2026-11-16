using System.Buffers.Text;
using System.Security.Cryptography;

namespace LicenseHub.Infrastructure.Signing;

/// <summary>
/// The ECDSA P-256 key that signs validation responses. Signatures use SHA-256 and the IEEE P1363
/// format (r || s, 64 bytes), the same as JWS ES256 and Web Crypto.
/// </summary>
public sealed class ResponseSigningKey : IDisposable
{
    public const string Algorithm = "ES256";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly ECDsa _ecdsa;
    private readonly Lock _gate = new();

    private ResponseSigningKey(ECDsa ecdsa, bool isEphemeral)
    {
        if (!IsP256(ecdsa))
        {
            ecdsa.Dispose();
            throw new InvalidOperationException("The signing key must be an ECDSA P-256 key.");
        }

        _ecdsa = ecdsa;
        IsEphemeral = isEphemeral;
        PublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        KeyId = ComputeKeyId(ecdsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>First 16 base64url characters of the SHA-256 of the public key (SubjectPublicKeyInfo DER).</summary>
    public string KeyId { get; }

    public string PublicKeyPem { get; }

    /// <summary>True for a key generated at startup because none was configured (Development only).</summary>
    public bool IsEphemeral { get; }

    public static ResponseSigningKey Generate(bool isEphemeral = false) =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256), isEphemeral);

    /// <summary>Loads a PKCS#8 private key given as PEM or as single-line base64 DER.</summary>
    public static ResponseSigningKey Load(string configuredKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredKey);

        var ecdsa = ECDsa.Create();
        try
        {
            var trimmed = configuredKey.Trim();
            if (trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                ecdsa.ImportFromPem(trimmed);
            }
            else
            {
                ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(trimmed), out _);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            ecdsa.Dispose();

            // Never echo the configured value: it is a secret.
            throw new InvalidOperationException(
                "Signing:PrivateKey is not a valid PKCS#8 ECDSA private key (PEM or base64 DER).",
                ex);
        }

        return new ResponseSigningKey(ecdsa, isEphemeral: false);
    }

    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Base64Url.EncodeToString(SHA256.HashData(subjectPublicKeyInfo))[..16];

    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        // ECDsa instances are not documented as thread-safe.
        lock (_gate)
        {
            return _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }

    public string ExportPrivateKeyPem()
    {
        lock (_gate)
        {
            return _ecdsa.ExportPkcs8PrivateKeyPem();
        }
    }

    /// <summary>Single-line form, convenient for .env files.</summary>
    public string ExportPrivateKeyBase64()
    {
        lock (_gate)
        {
            return Convert.ToBase64String(_ecdsa.ExportPkcs8PrivateKey());
        }
    }

    public void Dispose() => _ecdsa.Dispose();

    private static bool IsP256(ECDsa ecdsa)
    {
        var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve;
        return curve.IsNamed
            && (curve.Oid.Value == P256Oid || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "prime256v1");
    }
}
