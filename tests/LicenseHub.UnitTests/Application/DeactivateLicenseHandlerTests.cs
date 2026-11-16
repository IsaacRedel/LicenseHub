using LicenseHub.Application.Common;
using LicenseHub.Application.Licenses;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;

namespace LicenseHub.UnitTests.Application;

public sealed class DeactivateLicenseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly DeactivateLicenseHandler _handler;
    private readonly License _license;
    private readonly string _key;

    public DeactivateLicenseHandlerTests()
    {
        var product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);
        var key = LicenseKey.Generate();
        _license = License.Issue(product, "buyer@example.com", key.ComputeHash(), null, null, Now);
        _key = key.Value;
        _store.Products.Add(product);
        _store.Licenses.Add(_license);
        _store.Activations.Add(Activation.Create(_license.Id, "srv-01", Now));
        _handler = new DeactivateLicenseHandler(_store, _store, _store);
    }

    [Fact]
    public async Task HandleAsync_RemovesTheActivation()
    {
        var result = await Deactivate(_key.ToLowerInvariant(), "  srv-01 ");

        Assert.True(result.IsSuccess);
        Assert.Equal(_license.Id, result.Value.LicenseId);
        Assert.Equal("srv-01", result.Value.InstanceId);
        Assert.Empty(_store.Activations);
        Assert.Equal(1, _store.SaveCount);
    }

    [Theory]
    [InlineData("LH-0000-0000-0000-0000", "srv-01")]
    [InlineData("not-a-license-key", "srv-01")]
    [InlineData(null, "srv-02")]
    public async Task HandleAsync_ReturnsTheSameNotFoundForUnknownKeyOrInstance(string? key, string instanceId)
    {
        var result = await Deactivate(key ?? _key, instanceId);

        Assert.False(result.IsSuccess);
        Assert.Same(DeactivateLicenseHandler.NotActive, result.Error);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Single(_store.Activations);
    }

    [Theory]
    [InlineData("", "srv-01", "request.invalid_key")]
    [InlineData("LH-0000-0000-0000-0000", "", "request.invalid_instance_id")]
    public async Task HandleAsync_RejectsMalformedInput(string key, string instanceId, string expectedCode)
    {
        var result = await Deactivate(key, instanceId);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    private Task<Result<DeactivatedInstance>> Deactivate(string key, string instanceId) =>
        _handler.HandleAsync(new DeactivateLicenseCommand(key, instanceId), TestContext.Current.CancellationToken);
}
