using System.Text;
using System.Text.RegularExpressions;
using LicenseHub.Application.Common;
using LicenseHub.Application.Licenses;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class IssueLicenseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly IssueLicenseHandler _handler;
    private readonly Product _product = Product.Create("FiveM Garage", "fivem-garage", 3, Now);

    public IssueLicenseHandlerTests()
    {
        _store.Products.Add(_product);
        _handler = new IssueLicenseHandler(_store, _store, _store, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_IssuesActiveLicenseWithProductDefaults()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));

        Assert.True(result.IsSuccess);
        var license = Assert.Single(_store.Licenses);
        Assert.Equal(license.Id, result.Value.LicenseId);
        Assert.Equal(_product.Id, license.ProductId);
        Assert.Equal(LicenseStatus.Active, license.Status);
        Assert.Equal(3, license.MaxActivations);
        Assert.Equal(3, result.Value.MaxActivations);
        Assert.Null(license.ExpiresAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_UsesExplicitLimitAndExpiration()
    {
        var expiresAt = Now.AddDays(30);

        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com", 10, expiresAt));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.MaxActivations);
        Assert.Equal(expiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_ReturnsKeyWhoseHashIsTheStoredHash()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));

        Assert.Matches(new Regex("^LH(-[0-9A-HJKMNP-TV-Z]{4}){4}$"), result.Value.Key);
        Assert.True(LicenseKey.TryParse(result.Value.Key, out var key));
        Assert.Equal(key!.ComputeHash(), Assert.Single(_store.Licenses).KeyHash);
    }

    [Fact]
    public async Task HandleAsync_DoesNotStoreThePlainKey()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));
        var plainKey = result.Value.Key;
        var plainKeyBytes = Encoding.ASCII.GetBytes(plainKey);
        var license = Assert.Single(_store.Licenses);

        foreach (var property in typeof(License).GetProperties())
        {
            switch (property.GetValue(license))
            {
                case string text:
                    Assert.DoesNotContain(plainKey[3..], text, StringComparison.OrdinalIgnoreCase);
                    break;
                case byte[] bytes:
                    Assert.NotEqual(plainKeyBytes, bytes);
                    break;
            }
        }
    }

    [Fact]
    public async Task HandleAsync_KeepsTheKeyOutOfToString()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));

        Assert.DoesNotContain(result.Value.Key, result.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_IssuesDifferentKeysForEachLicense()
    {
        var first = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));
        var second = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com"));

        Assert.NotEqual(first.Value.Key, second.Value.Key);
        Assert.NotEqual(_store.Licenses[0].KeyHash, _store.Licenses[1].KeyHash);
    }

    [Fact]
    public async Task HandleAsync_ReturnsProductNotFound()
    {
        var result = await Issue(new IssueLicenseCommand(Guid.NewGuid(), "buyer@example.com"));

        Assert.False(result.IsSuccess);
        Assert.Equal("license.product_not_found", result.Error!.Code);
        Assert.Empty(_store.Licenses);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsExpirationInThePast()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "buyer@example.com", ExpiresAt: Now.AddMinutes(-1)));

        Assert.False(result.IsSuccess);
        Assert.Equal("license.invalid_expiration", result.Error!.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsInvalidCustomerEmail()
    {
        var result = await Issue(new IssueLicenseCommand(_product.Id, "not-an-email"));

        Assert.False(result.IsSuccess);
        Assert.Equal("license.invalid_customer_email", result.Error!.Code);
        Assert.Equal(0, _store.SaveCount);
    }

    private Task<Result<IssuedLicense>> Issue(IssueLicenseCommand command) =>
        _handler.HandleAsync(command, TestContext.Current.CancellationToken);
}
