using LicenseHub.Application.Common;
using LicenseHub.Application.Products;
using LicenseHub.Domain.Products;

namespace LicenseHub.UnitTests.Application;

public sealed class UpdateProductHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly UpdateProductHandler _handler;
    private readonly Product _product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);

    public UpdateProductHandlerTests()
    {
        _store.Products.Add(_product);
        _handler = new UpdateProductHandler(_store, _store);
    }

    [Fact]
    public async Task HandleAsync_UpdatesTheGivenFields()
    {
        var result = await Update(_product.Id, "Garage Pro", 4);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Garage Pro", 4, "fivem-garage"), (_product.Name, _product.DefaultMaxActivations, _product.Slug));
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFound()
    {
        var result = await Update(Guid.CreateVersion7(), "Name", null);

        Assert.Same(UpdateProductHandler.NotFound, result.Error);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Theory]
    [InlineData(" ", null, "product.invalid_name", "name")]
    [InlineData(null, 0, "product.invalid_max_activations", "defaultMaxActivations")]
    public async Task HandleAsync_RejectsInvalidValuesWithoutSaving(string? name, int? max, string code, string field)
    {
        var result = await Update(_product.Id, name, max);

        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(field, result.Error.Field);
        Assert.Equal(0, _store.SaveCount);
    }

    private Task<Result<Guid>> Update(Guid id, string? name, int? max) =>
        _handler.HandleAsync(new UpdateProductCommand(id, name, max), TestContext.Current.CancellationToken);
}
