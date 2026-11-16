using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;

namespace LicenseHub.UnitTests.Domain;

public sealed class LicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Product Product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);
    private static readonly byte[] Hash = LicenseKey.Generate().ComputeHash();

    [Fact]
    public void Issue_CreatesActiveLicenseUsingProductDefaults()
    {
        var license = License.Issue(Product, "buyer@example.com", Hash, maxActivations: null, expiresAt: null, Now);

        Assert.NotEqual(Guid.Empty, license.Id);
        Assert.Equal(Product.Id, license.ProductId);
        Assert.Equal("buyer@example.com", license.CustomerEmail);
        Assert.Equal(Hash, license.KeyHash);
        Assert.Equal(LicenseStatus.Active, license.Status);
        Assert.Equal(2, license.MaxActivations);
        Assert.Null(license.ExpiresAt);
        Assert.Null(license.RevokedAt);
        Assert.Equal(Now, license.CreatedAt);
    }

    [Fact]
    public void Issue_UsesExplicitActivationLimitAndExpiration()
    {
        var expiresAt = Now.AddDays(30);

        var license = License.Issue(Product, "buyer@example.com", Hash, maxActivations: 5, expiresAt, Now);

        Assert.Equal(5, license.MaxActivations);
        Assert.Equal(expiresAt, license.ExpiresAt);
    }

    [Fact]
    public void Issue_CopiesTheHashInsteadOfKeepingTheCallerArray()
    {
        var hash = LicenseKey.Generate().ComputeHash();

        var license = License.Issue(Product, "buyer@example.com", hash, null, null, Now);
        hash[0] ^= 0xFF;

        Assert.NotEqual(hash, license.KeyHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Issue_RejectsExpirationNotInTheFuture(int offsetSeconds)
    {
        var error = Assert.Throws<DomainException>(
            () => License.Issue(Product, "buyer@example.com", Hash, null, Now.AddSeconds(offsetSeconds), Now));

        Assert.Equal("license.invalid_expiration", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Buyer <buyer@example.com>")]
    [InlineData("buyer@")]
    public void Issue_RejectsInvalidCustomerEmail(string email)
    {
        var error = Assert.Throws<DomainException>(() => License.Issue(Product, email, Hash, null, null, Now));

        Assert.Equal("license.invalid_customer_email", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Issue_RejectsHashWithWrongLength(int length)
    {
        var error = Assert.Throws<DomainException>(
            () => License.Issue(Product, "buyer@example.com", new byte[length], null, null, Now));

        Assert.Equal("license.invalid_key_hash", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Limits.MaxActivations + 1)]
    public void Issue_RejectsActivationLimitOutOfRange(int maxActivations)
    {
        var error = Assert.Throws<DomainException>(
            () => License.Issue(Product, "buyer@example.com", Hash, maxActivations, null, Now));

        Assert.Equal("license.invalid_max_activations", error.Code);
    }
}
