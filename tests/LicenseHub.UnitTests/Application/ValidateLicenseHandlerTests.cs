using LicenseHub.Application.Common;
using LicenseHub.Application.Licenses;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class ValidateLicenseHandlerTests
{
    private const string Nonce = "client-nonce-0123456789";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ValidateLicenseHandler _handler;
    private readonly Product _product;
    private readonly Product _otherProduct;

    public ValidateLicenseHandlerTests()
    {
        _product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);
        _otherProduct = Product.Create("FiveM Racing", "fivem-racing", 2, Now);
        _store.Products.AddRange([_product, _otherProduct]);
        _handler = new ValidateLicenseHandler(_store, _store, _store, _store, _store, _time);
    }

    [Fact]
    public async Task HandleAsync_ActivatesNewInstanceAndEchoesNonce()
    {
        var (license, key) = AddLicense();

        var result = await Validate(key, instanceId: "srv-01");

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidationOutcome.Valid, result.Value.Outcome);
        Assert.Equal(license.Id, result.Value.LicenseId);
        Assert.Equal(Nonce, result.Value.Nonce);
        Assert.Equal("srv-01", result.Value.InstanceId);
        Assert.Equal(Now, result.Value.IssuedAt);
        var activation = Assert.Single(_store.Activations);
        Assert.Equal("srv-01", activation.InstanceId);
        Assert.Equal(1, _store.LockCount);
    }

    [Fact]
    public async Task HandleAsync_BindsTheVerdictToTheRequestedProductAndKey()
    {
        var (_, key) = AddLicense();
        var sentKey = $"  {key.ToLowerInvariant()}";

        var valid = await Validate(sentKey, productSlug: "FiveM-Garage");
        var mismatch = await Validate(sentKey, productSlug: "fivem-racing");

        Assert.Equal("fivem-garage", valid.Value.ProductSlug);
        Assert.Equal("fivem-racing", mismatch.Value.ProductSlug);
        Assert.Equal(ValidateLicenseHandler.ComputeKeyFingerprint(sentKey), valid.Value.KeyFingerprint);
        Assert.NotEqual(ValidateLicenseHandler.ComputeKeyFingerprint(key), valid.Value.KeyFingerprint);
    }

    [Fact]
    public void ComputeKeyFingerprint_HashesTheKeyExactlyAsSent()
    {
        // Reference: printf ' lh-0123-4567-89ab-cdef' | openssl dgst -sha256 -binary | base64url | cut -c1-16
        Assert.Equal("vnWY8h7rOBEVyrIY", ValidateLicenseHandler.ComputeKeyFingerprint(" lh-0123-4567-89ab-cdef"));
    }

    [Fact]
    public async Task HandleAsync_RefreshesInstanceThatIsAlreadyActive()
    {
        var (license, key) = AddLicense(maxActivations: 1);
        var existing = Activation.Create(license.Id, "srv-01", Now.AddDays(-1));
        _store.Activations.Add(existing);

        var result = await Validate(key, instanceId: "srv-01");

        Assert.Equal(ValidationOutcome.Valid, result.Value.Outcome);
        Assert.Same(existing, Assert.Single(_store.Activations));
        Assert.Equal(Now, existing.LastValidatedAt);
    }

    [Fact]
    public async Task HandleAsync_RejectsNewInstanceWhenLimitIsReached()
    {
        var (license, key) = AddLicense(maxActivations: 1);
        _store.Activations.Add(Activation.Create(license.Id, "srv-01", Now));

        var result = await Validate(key, instanceId: "srv-02");

        Assert.Equal(ValidationOutcome.ActivationLimitReached, result.Value.Outcome);
        Assert.Equal(license.Id, result.Value.LicenseId);
        Assert.Single(_store.Activations);
    }

    [Theory]
    [InlineData("LH-0000-0000-0000-0000")]
    [InlineData("not-a-license-key")]
    public async Task HandleAsync_ReturnsUnknownKey(string key)
    {
        AddLicense();

        var result = await Validate(key);

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidationOutcome.UnknownKey, result.Value.Outcome);
        Assert.Null(result.Value.LicenseId);
        Assert.Null(result.Value.ExpiresAt);
        Assert.Empty(_store.Activations);
        Assert.Equal(0, _store.TransactionCount);
    }

    [Theory]
    [InlineData("fivem-racing")]
    [InlineData("does-not-exist")]
    public async Task HandleAsync_ReturnsProductMismatchWithoutRevealingTheLicense(string productSlug)
    {
        var (license, key) = AddLicense(expiresAt: Now.AddDays(10));

        var result = await Validate(key, productSlug: productSlug);

        Assert.Equal(ValidationOutcome.ProductMismatch, result.Value.Outcome);
        Assert.Null(result.Value.LicenseId);
        Assert.Null(result.Value.ExpiresAt);
        Assert.Equal(license.Id, Assert.Single(_store.ValidationLogs).LicenseId);
        Assert.Empty(_store.Activations);
    }

    [Fact]
    public async Task HandleAsync_ReturnsRevoked()
    {
        var (license, key) = AddLicense();
        license.Revoke(Now.AddMinutes(-1));

        var result = await Validate(key);

        Assert.Equal(ValidationOutcome.Revoked, result.Value.Outcome);
        Assert.Equal(license.Id, result.Value.LicenseId);
        Assert.Empty(_store.Activations);
        Assert.Equal(0, _store.TransactionCount);
    }

    [Fact]
    public async Task HandleAsync_ReturnsExpired()
    {
        var expiresAt = Now.AddDays(1);
        var (license, key) = AddLicense(expiresAt: expiresAt);
        _time.SetUtcNow(expiresAt.AddSeconds(1));

        var result = await Validate(key, timestamp: expiresAt.AddSeconds(1).ToUnixTimeSeconds());

        Assert.Equal(ValidationOutcome.Expired, result.Value.Outcome);
        Assert.Equal(license.Id, result.Value.LicenseId);
        Assert.Equal(expiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_LogsEveryAttemptWithOutcomeAndIp()
    {
        var (license, key) = AddLicense(maxActivations: 1);

        await Validate(key, instanceId: "srv-01");
        await Validate(key, instanceId: "srv-02");
        await Validate("LH-0000-0000-0000-0000");

        Assert.Collection(
            _store.ValidationLogs,
            log => AssertLog(log, license.Id, ValidationOutcome.Valid),
            log => AssertLog(log, license.Id, ValidationOutcome.ActivationLimitReached),
            log => AssertLog(log, null, ValidationOutcome.UnknownKey));
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    [InlineData(0)]
    public async Task HandleAsync_AcceptsTimestampInsideTheWindow(int offsetSeconds)
    {
        var (_, key) = AddLicense();

        var result = await Validate(key, timestamp: Now.AddSeconds(offsetSeconds).ToUnixTimeSeconds());

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public async Task HandleAsync_RejectsTimestampOutsideTheWindowWithoutLogging(int offsetSeconds)
    {
        var (_, key) = AddLicense();

        var result = await Validate(key, timestamp: Now.AddSeconds(offsetSeconds).ToUnixTimeSeconds());

        Assert.False(result.IsSuccess);
        Assert.Equal("request.timestamp_out_of_window", result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Empty(_store.ValidationLogs);
        Assert.Equal(0, _store.SaveCount);
    }

    [Theory]
    [InlineData(null, "fivem-garage", "srv-01", Nonce, "request.invalid_key")]
    [InlineData("LH-0000-0000-0000-0000", "Not A Slug", "srv-01", Nonce, "request.invalid_product_slug")]
    [InlineData("LH-0000-0000-0000-0000", "fivem-garage", " ", Nonce, "request.invalid_instance_id")]
    [InlineData("LH-0000-0000-0000-0000", "fivem-garage", "srv-01", "too-short", "request.invalid_nonce")]
    [InlineData("LH-0000-0000-0000-0000", "fivem-garage", "srv-01", "has spaces in the nonce", "request.invalid_nonce")]
    [InlineData("LH-0000-0000-0000-0000", "fivem-garage", "srv-01", "AAAAAAAAAAAAAAAA\n", "request.invalid_nonce")]
    [InlineData("LH-0000-0000-0000-0000", "fivem-garage", "srv-01", null, "request.invalid_nonce")]
    public async Task HandleAsync_RejectsMalformedInput(string? key, string slug, string instanceId, string? nonce, string expectedCode)
    {
        var result = await _handler.HandleAsync(
            new ValidateLicenseCommand(key, slug, instanceId, nonce, Now.ToUnixTimeSeconds()),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Empty(_store.ValidationLogs);
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingTimestamp()
    {
        var result = await _handler.HandleAsync(
            new ValidateLicenseCommand("LH-0000-0000-0000-0000", "fivem-garage", "srv-01", Nonce, null),
            TestContext.Current.CancellationToken);

        Assert.Equal("request.invalid_timestamp", result.Error!.Code);
    }

    private static void AssertLog(ValidationLog log, Guid? licenseId, ValidationOutcome outcome)
    {
        Assert.Equal(licenseId, log.LicenseId);
        Assert.Equal(outcome, log.Outcome);
        Assert.Equal("203.0.113.7", log.IpAddress);
        Assert.Equal(Now, log.CreatedAt);
    }

    private (License License, string Key) AddLicense(int? maxActivations = null, DateTimeOffset? expiresAt = null)
    {
        var key = LicenseKey.Generate();
        var license = License.Issue(_product, "buyer@example.com", key.ComputeHash(), maxActivations, expiresAt, Now.AddDays(-1));
        _store.Licenses.Add(license);
        return (license, key.Value);
    }

    private Task<Result<LicenseValidation>> Validate(
        string key,
        string productSlug = "fivem-garage",
        string instanceId = "srv-01",
        long? timestamp = null) =>
        _handler.HandleAsync(
            new ValidateLicenseCommand(key, productSlug, instanceId, Nonce, timestamp ?? Now.ToUnixTimeSeconds(), "203.0.113.7"),
            TestContext.Current.CancellationToken);
}
