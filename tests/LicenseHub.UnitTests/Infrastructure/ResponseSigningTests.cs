using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseHub.Application.Licenses;
using LicenseHub.Domain.Validation;
using LicenseHub.Infrastructure.Signing;

namespace LicenseHub.UnitTests.Infrastructure;

public sealed class ResponseSigningTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly ResponseSigningKey _key = ResponseSigningKey.Generate();
    private readonly ValidationResponseSigner _signer;

    public ResponseSigningTests()
    {
        _signer = new ValidationResponseSigner(_key);
    }

    public void Dispose() => _key.Dispose();

    [Fact]
    public void Sign_ProducesSignatureThatVerifiesWithThePublicKey()
    {
        var response = _signer.Sign(NewValidation());

        Assert.True(Verify(_key.PublicKeyPem, response));
        Assert.Equal(_key.KeyId, response.KeyId);
        Assert.Equal(64, Base64Url.DecodeFromChars(response.Signature).Length);
    }

    [Fact]
    public void Verify_FailsWhenPayloadIsTampered()
    {
        var response = _signer.Sign(NewValidation(ValidationOutcome.Revoked));
        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(response.Payload));
        var forged = payload.Replace("\"valid\":false", "\"valid\":true", StringComparison.Ordinal);
        Assert.NotEqual(payload, forged);

        var tampered = response with { Payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(forged)) };

        Assert.False(Verify(_key.PublicKeyPem, tampered));
    }

    [Fact]
    public void Verify_FailsForEquivalentButReserializedJson()
    {
        var response = _signer.Sign(NewValidation());
        var bytes = Base64Url.DecodeFromChars(response.Payload);
        var reserialized = JsonSerializer.SerializeToUtf8Bytes(
            JsonDocument.Parse(bytes).RootElement,
            new JsonSerializerOptions { WriteIndented = true });

        var tampered = response with { Payload = Base64Url.EncodeToString(reserialized) };

        Assert.False(Verify(_key.PublicKeyPem, tampered));
    }

    [Fact]
    public void Verify_FailsWithAnotherKey()
    {
        using var otherKey = ResponseSigningKey.Generate();

        var response = _signer.Sign(NewValidation());

        Assert.False(Verify(otherKey.PublicKeyPem, response));
        Assert.NotEqual(_key.KeyId, otherKey.KeyId);
    }

    [Fact]
    public void Payload_ContainsAllFieldsInUnixSeconds()
    {
        var licenseId = Guid.CreateVersion7(Now);
        var expiresAt = Now.AddDays(30);
        var validation = new LicenseValidation(
            ValidationOutcome.Valid, licenseId, expiresAt, "fivem-garage", "fp-0123456789abc", "nonce-0123456789abc", "srv-01", Now);

        var json = Decode(_signer.Sign(validation));

        Assert.True(json.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reason").ValueKind);
        Assert.Equal(licenseId, json.GetProperty("licenseId").GetGuid());
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), json.GetProperty("expiresAt").GetInt64());
        Assert.Equal("fivem-garage", json.GetProperty("productSlug").GetString());
        Assert.Equal("fp-0123456789abc", json.GetProperty("keyFingerprint").GetString());
        Assert.Equal("nonce-0123456789abc", json.GetProperty("nonce").GetString());
        Assert.Equal("srv-01", json.GetProperty("instanceId").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), json.GetProperty("issuedAt").GetInt64());
    }

    [Theory]
    [InlineData(ValidationOutcome.UnknownKey, "unknown_key")]
    [InlineData(ValidationOutcome.ProductMismatch, "product_mismatch")]
    [InlineData(ValidationOutcome.Revoked, "revoked")]
    [InlineData(ValidationOutcome.Expired, "expired")]
    [InlineData(ValidationOutcome.ActivationLimitReached, "activation_limit_reached")]
    public void Payload_MapsRejectionsToReasonsAndKeepsNullFields(ValidationOutcome outcome, string reason)
    {
        var json = Decode(_signer.Sign(NewValidation(outcome)));

        Assert.False(json.GetProperty("valid").GetBoolean());
        Assert.Equal(reason, json.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("licenseId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public void Load_AcceptsPemAndBase64AndKeepsTheKeyId()
    {
        using var fromPem = ResponseSigningKey.Load(_key.ExportPrivateKeyPem());
        using var fromBase64 = ResponseSigningKey.Load(_key.ExportPrivateKeyBase64());

        Assert.Equal(_key.KeyId, fromPem.KeyId);
        Assert.Equal(_key.KeyId, fromBase64.KeyId);
        Assert.False(fromPem.IsEphemeral);
    }

    [Fact]
    public void KeyId_IsDerivedFromThePublicKey()
    {
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(_key.PublicKeyPem);

        var expected = ResponseSigningKey.ComputeKeyId(publicKey.ExportSubjectPublicKeyInfo());

        Assert.Equal(expected, _key.KeyId);
        Assert.Equal(16, _key.KeyId.Length);
    }

    [Theory]
    [InlineData("not a key")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----")]
    public void Load_RejectsInvalidKeyWithoutEchoingIt(string configured)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ResponseSigningKey.Load(configured));

        Assert.DoesNotContain(configured, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsCurvesOtherThanP256()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var error = Assert.Throws<InvalidOperationException>(() => ResponseSigningKey.Load(p384.ExportPkcs8PrivateKeyPem()));

        Assert.Contains("P-256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAPublicKey()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ResponseSigningKey.Load(_key.PublicKeyPem));

        Assert.Contains("public key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_MarksEphemeralKeys()
    {
        using var ephemeral = ResponseSigningKey.Generate(isEphemeral: true);

        Assert.True(ephemeral.IsEphemeral);
    }

    /// <summary>What a client does: verify the decoded payload bytes with the public key.</summary>
    private static bool Verify(string publicKeyPem, SignedResponse response)
    {
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(publicKeyPem);

        return publicKey.VerifyData(
            Base64Url.DecodeFromChars(response.Payload),
            Base64Url.DecodeFromChars(response.Signature),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static JsonElement Decode(SignedResponse response) =>
        JsonDocument.Parse(Base64Url.DecodeFromChars(response.Payload)).RootElement;

    private static LicenseValidation NewValidation(ValidationOutcome outcome = ValidationOutcome.Valid) =>
        new(
            outcome,
            outcome == ValidationOutcome.Valid ? Guid.CreateVersion7(Now) : null,
            null,
            "fivem-garage",
            "fp-0123456789abc",
            "nonce-0123456789abc",
            "srv-01",
            Now);
}
