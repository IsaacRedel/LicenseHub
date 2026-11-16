using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.UnitTests.Domain;

public sealed class LicenseActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Product Product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);

    [Fact]
    public void CheckUsable_ReturnsValidForActiveLicense()
    {
        var license = NewLicense();

        Assert.Equal(ValidationOutcome.Valid, license.CheckUsable(Now));
    }

    [Fact]
    public void CheckUsable_ReturnsRevokedAfterRevoke()
    {
        var license = NewLicense();

        license.Revoke(Now.AddMinutes(1), "Chargeback");

        Assert.Equal(ValidationOutcome.Revoked, license.CheckUsable(Now.AddMinutes(2)));
        Assert.Equal(LicenseStatus.Revoked, license.Status);
        Assert.Equal(Now.AddMinutes(1), license.RevokedAt);
    }

    [Fact]
    public void Revoke_KeepsTheFirstRevocationDate()
    {
        var license = NewLicense();

        license.Revoke(Now.AddMinutes(1), "Chargeback");
        license.Revoke(Now.AddMinutes(5), "Chargeback");

        Assert.Equal(Now.AddMinutes(1), license.RevokedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CheckUsable_ReturnsExpiredAtOrAfterExpiration(int secondsAfterExpiry)
    {
        var expiresAt = Now.AddDays(1);
        var license = NewLicense(expiresAt: expiresAt);

        Assert.Equal(ValidationOutcome.Valid, license.CheckUsable(expiresAt.AddSeconds(-1)));
        Assert.Equal(ValidationOutcome.Expired, license.CheckUsable(expiresAt.AddSeconds(secondsAfterExpiry)));
    }

    [Fact]
    public void Activate_CreatesActivationForNewInstanceBelowLimit()
    {
        var license = NewLicense();

        var result = license.Activate([], "  srv-01  ", Now);

        Assert.True(result.IsValid);
        Assert.True(result.IsNewActivation);
        Assert.Equal("srv-01", result.Activation!.InstanceId);
        Assert.Equal(license.Id, result.Activation.LicenseId);
        Assert.Equal(Now, result.Activation.FirstValidatedAt);
    }

    [Fact]
    public void Activate_RefreshesInstanceThatIsAlreadyActive()
    {
        var license = NewLicense();
        var existing = Activation.Create(license.Id, "srv-01", Now);
        var later = Now.AddHours(3);

        var result = license.Activate([existing], "srv-01", later);

        Assert.True(result.IsValid);
        Assert.False(result.IsNewActivation);
        Assert.Same(existing, result.Activation);
        Assert.Equal(Now, existing.FirstValidatedAt);
        Assert.Equal(later, existing.LastValidatedAt);
    }

    [Fact]
    public void Activate_AcceptsActiveInstanceEvenWhenLicenseIsAtTheLimit()
    {
        var license = NewLicense(maxActivations: 1);
        var existing = Activation.Create(license.Id, "srv-01", Now);

        var result = license.Activate([existing], "srv-01", Now.AddMinutes(1));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Activate_RejectsNewInstanceWhenLimitIsReached()
    {
        var license = NewLicense(maxActivations: 2);
        Activation[] activations =
        [
            Activation.Create(license.Id, "srv-01", Now),
            Activation.Create(license.Id, "srv-02", Now),
        ];

        var result = license.Activate(activations, "srv-03", Now);

        Assert.Equal(ValidationOutcome.ActivationLimitReached, result.Outcome);
        Assert.Null(result.Activation);
        Assert.False(result.IsNewActivation);
    }

    [Fact]
    public void Activate_TreatsInstanceIdsAsCaseSensitive()
    {
        var license = NewLicense(maxActivations: 1);
        var existing = Activation.Create(license.Id, "srv-01", Now);

        var result = license.Activate([existing], "SRV-01", Now);

        Assert.Equal(ValidationOutcome.ActivationLimitReached, result.Outcome);
    }

    [Fact]
    public void Activate_RejectsRevokedLicenseWithoutTouchingActivations()
    {
        var license = NewLicense();
        var existing = Activation.Create(license.Id, "srv-01", Now);
        license.Revoke(Now, "Chargeback");

        var result = license.Activate([existing], "srv-01", Now.AddHours(1));

        Assert.Equal(ValidationOutcome.Revoked, result.Outcome);
        Assert.Equal(Now, existing.LastValidatedAt);
    }

    [Fact]
    public void Activate_RejectsExpiredLicense()
    {
        var license = NewLicense(expiresAt: Now.AddDays(1));

        var result = license.Activate([], "srv-01", Now.AddDays(2));

        Assert.Equal(ValidationOutcome.Expired, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Activate_RejectsEmptyInstanceId(string instanceId)
    {
        var license = NewLicense();

        var error = Assert.Throws<DomainException>(() => license.Activate([], instanceId, Now));

        Assert.Equal("activation.invalid_instance_id", error.Code);
    }

    [Fact]
    public void Activate_RejectsActivationsOfAnotherLicense()
    {
        var license = NewLicense();
        var foreign = Activation.Create(Guid.CreateVersion7(), "srv-01", Now);

        Assert.Throws<ArgumentException>(() => license.Activate([foreign], "srv-02", Now));
    }

    [Fact]
    public void Touch_NeverMovesLastValidationBackwards()
    {
        var activation = Activation.Create(Guid.CreateVersion7(), "srv-01", Now);

        activation.Touch(Now.AddMinutes(-5));

        Assert.Equal(Now, activation.LastValidatedAt);
    }

    private static License NewLicense(int? maxActivations = null, DateTimeOffset? expiresAt = null) =>
        License.Issue(Product, "buyer@example.com", LicenseKey.Generate().ComputeHash(), maxActivations, expiresAt, Now);
}
