using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Products;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class CreateProductHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly CreateProductHandler _handler;

    public CreateProductHandlerTests()
    {
        _handler = new CreateProductHandler(_store, _store, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_CreatesAndSavesProduct()
    {
        var result = await _handler.HandleAsync(
            new CreateProductCommand("FiveM Garage", "fivem-garage", 2),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_store.Products);
        Assert.Equal(saved.Id, result.Value.Id);
        Assert.Equal("fivem-garage", result.Value.Slug);
        Assert.Equal(2, result.Value.DefaultMaxActivations);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsDuplicateSlugIgnoringCase()
    {
        await _handler.HandleAsync(new CreateProductCommand("First", "fivem-garage", 1), TestContext.Current.CancellationToken);

        var result = await _handler.HandleAsync(
            new CreateProductCommand("Second", "FiveM-Garage", 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("product.slug_taken", result.Error!.Code);
        Assert.Single(_store.Products);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_MapsUniqueViolationOnSaveToSlugTaken()
    {
        _store.FailNextSaveWith = new UniqueConstraintException("duplicate slug");

        var result = await _handler.HandleAsync(
            new CreateProductCommand("Racing", "racing", 1),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("product.slug_taken", result.Error!.Code);
        Assert.Empty(_store.Products);
    }

    [Theory]
    [InlineData("", "valid-slug", 1, "product.invalid_name")]
    [InlineData("Name", "Invalid Slug", 1, "product.invalid_slug")]
    [InlineData("Name", "valid-slug", 0, "product.invalid_max_activations")]
    public async Task HandleAsync_ReturnsDomainErrorWithoutSaving(string name, string slug, int maxActivations, string expectedCode)
    {
        var result = await _handler.HandleAsync(
            new CreateProductCommand(name, slug, maxActivations),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(0, _store.SaveCount);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
