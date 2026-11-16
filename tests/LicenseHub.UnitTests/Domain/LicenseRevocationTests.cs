using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.UnitTests.Domain;

public sealed class LicenseRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Product Product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);

    [Fact]
    public void Revoke_StoresDateReasonAndAdmin()
    {
        var license = NewLicense();
        var adminId = Guid.CreateVersion7(Now);

        var revoked = license.Revoke(Now.AddHours(1), "  Chargeback  ", adminId);

        Assert.True(revoked);
        Assert.Equal(LicenseStatus.Revoked, license.Status);
        Assert.Equal(Now.AddHours(1), license.RevokedAt);
        Assert.Equal("Chargeback", license.RevocationReason);
        Assert.Equal(adminId, license.RevokedByAdminId);
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstRevocation()
    {
        var license = NewLicense();
        var firstAdmin = Guid.CreateVersion7(Now);
        license.Revoke(Now.AddHours(1), "Chargeback", firstAdmin);

        var revokedAgain = license.Revoke(Now.AddHours(2), "Duplicate", Guid.CreateVersion7(Now));

        Assert.False(revokedAgain);
        Assert.Equal(Now.AddHours(1), license.RevokedAt);
        Assert.Equal("Chargeback", license.RevocationReason);
        Assert.Equal(firstAdmin, license.RevokedByAdminId);
    }

    [Fact]
    public void RevokedLicense_FailsValidationEvenForAnActiveInstance()
    {
        var license = NewLicense();
        var activation = Activation.Create(license.Id, "srv-01", Now);
        license.Revoke(Now.AddMinutes(1), "Chargeback");

        var result = license.Activate([activation], "srv-01", Now.AddMinutes(2));

        Assert.Equal(ValidationOutcome.Revoked, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoke_RequiresAReason(string reason)
    {
        var license = NewLicense();

        var error = Assert.Throws<DomainException>(() => license.Revoke(Now, reason));

        Assert.Equal("license.invalid_revocation_reason", error.Code);
        Assert.Equal(LicenseStatus.Active, license.Status);
    }

    [Fact]
    public void Revoke_RejectsReasonLongerThanLimit()
    {
        var license = NewLicense();

        var error = Assert.Throws<DomainException>(
            () => license.Revoke(Now, new string('x', Limits.RevocationReasonLength + 1)));

        Assert.Equal("license.invalid_revocation_reason", error.Code);
    }

    [Fact]
    public void EffectiveStatus_TreatsPastExpirationAsExpired()
    {
        var license = NewLicense(expiresAt: Now.AddDays(1));

        Assert.Equal(LicenseStatus.Active, license.EffectiveStatus(Now));
        Assert.Equal(LicenseStatus.Expired, license.EffectiveStatus(Now.AddDays(1)));

        license.Revoke(Now.AddDays(2), "Chargeback");
        Assert.Equal(LicenseStatus.Revoked, license.EffectiveStatus(Now.AddDays(2)));
    }

    private static License NewLicense(DateTimeOffset? expiresAt = null) =>
        License.Issue(Product, "buyer@example.com", LicenseKey.Generate().ComputeHash(), null, expiresAt, Now);
}
