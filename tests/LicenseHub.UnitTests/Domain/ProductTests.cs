using LicenseHub.Domain.Common;
using LicenseHub.Domain.Products;

namespace LicenseHub.UnitTests.Domain;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsFieldsAndNormalizesInput()
    {
        var product = Product.Create("  FiveM Garage  ", "  FiveM-Garage ", 3, Now);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("FiveM Garage", product.Name);
        Assert.Equal("fivem-garage", product.Slug);
        Assert.Equal(3, product.DefaultMaxActivations);
        Assert.Equal(Now, product.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("acentuação")]
    public void Create_RejectsInvalidSlug(string slug)
    {
        var error = Assert.Throws<DomainException>(() => Product.Create("Product", slug, 1, Now));

        Assert.Equal("product.invalid_slug", error.Code);
    }

    [Fact]
    public void Create_RejectsSlugLongerThanLimit()
    {
        var slug = new string('a', Limits.ProductSlugLength + 1);

        var error = Assert.Throws<DomainException>(() => Product.Create("Product", slug, 1, Now));

        Assert.Equal("product.invalid_slug", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyName(string name)
    {
        var error = Assert.Throws<DomainException>(() => Product.Create(name, "product", 1, Now));

        Assert.Equal("product.invalid_name", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Limits.MaxActivations + 1)]
    public void Create_RejectsActivationLimitOutOfRange(int maxActivations)
    {
        var error = Assert.Throws<DomainException>(() => Product.Create("Product", "product", maxActivations, Now));

        Assert.Equal("product.invalid_max_activations", error.Code);
    }

    [Fact]
    public void Update_ChangesOnlyTheGivenFieldsAndKeepsTheSlug()
    {
        var product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);

        product.Update(" Garage Pro ", null);
        Assert.Equal(("Garage Pro", 2, "fivem-garage"), (product.Name, product.DefaultMaxActivations, product.Slug));

        product.Update(null, 5);
        Assert.Equal(("Garage Pro", 5), (product.Name, product.DefaultMaxActivations));
    }

    [Fact]
    public void Update_RejectsInvalidValuesWithoutPartialChanges()
    {
        var product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);

        var error = Assert.Throws<DomainException>(() => product.Update("New name", 0));

        Assert.Equal("product.invalid_max_activations", error.Code);
        Assert.Equal(("FiveM Garage", 2), (product.Name, product.DefaultMaxActivations));
    }
}
