using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseHub.Application.Licenses;
using LicenseHub.Domain.Validation;

namespace LicenseHub.Infrastructure.Signing;

/// <summary>
/// What the API returns for a validation: <see cref="Payload"/> is the base64url JSON that was signed,
/// and <see cref="Signature"/> is the base64url ES256 signature (IEEE P1363) over those exact bytes.
/// </summary>
public sealed record SignedResponse(string Payload, string Signature, string KeyId);

/// <summary>The signed validation verdict. Times are Unix seconds (UTC).</summary>
public sealed record ValidationPayload(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("licenseId")] Guid? LicenseId,
    [property: JsonPropertyName("expiresAt")] long? ExpiresAt,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("instanceId")] string InstanceId,
    [property: JsonPropertyName("issuedAt")] long IssuedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static ValidationPayload From(LicenseValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidationPayload(
            validation.IsValid,
            ReasonFor(validation.Outcome),
            validation.LicenseId,
            validation.ExpiresAt?.ToUnixTimeSeconds(),
            validation.Nonce,
            validation.InstanceId,
            validation.IssuedAt.ToUnixTimeSeconds());
    }

    /// <summary>The stable reason codes of the public API; null when the license is valid.</summary>
    public static string? ReasonFor(ValidationOutcome outcome) => outcome switch
    {
        ValidationOutcome.Valid => null,
        ValidationOutcome.UnknownKey => "unknown_key",
        ValidationOutcome.ProductMismatch => "product_mismatch",
        ValidationOutcome.Revoked => "revoked",
        ValidationOutcome.Expired => "expired",
        ValidationOutcome.ActivationLimitReached => "activation_limit_reached",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown validation outcome."),
    };

    public byte[] ToUtf8Json() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);

    public static ValidationPayload Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<ValidationPayload>(utf8Json, SerializerOptions)
        ?? throw new JsonException("Empty validation payload.");
}

public sealed class ValidationResponseSigner(ResponseSigningKey key)
{
    public string KeyId => key.KeyId;

    public SignedResponse Sign(LicenseValidation validation)
    {
        // Serialize once and sign those exact bytes; the client verifies the bytes it decodes.
        var payload = ValidationPayload.From(validation).ToUtf8Json();
        var signature = key.Sign(payload);

        return new SignedResponse(Base64Url.EncodeToString(payload), Base64Url.EncodeToString(signature), key.KeyId);
    }
}
