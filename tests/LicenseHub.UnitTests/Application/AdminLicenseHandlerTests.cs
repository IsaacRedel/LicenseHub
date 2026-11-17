using LicenseHub.Application.Admins;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class AdminLicenseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly License _license;

    public AdminLicenseHandlerTests()
    {
        var product = Product.Create("FiveM Garage", "fivem-garage", 2, Now);
        _license = License.Issue(product, "buyer@example.com", LicenseKey.Generate().ComputeHash(), null, null, Now);
        _store.Products.Add(product);
        _store.Licenses.Add(_license);
    }

    [Fact]
    public async Task Revoke_StoresReasonAndAdminUnderTheLock()
    {
        var adminId = Guid.CreateVersion7(Now);

        var result = await new RevokeLicenseHandler(_store, _store, _time)
            .HandleAsync(new RevokeLicenseCommand(_license.Id, "Chargeback", adminId), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal((LicenseStatus.Revoked, "Chargeback", adminId), (_license.Status, _license.RevocationReason, _license.RevokedByAdminId));
        Assert.Equal(1, _store.LockCount);
    }

    [Fact]
    public async Task Revoke_IsIdempotent()
    {
        var handler = new RevokeLicenseHandler(_store, _store, _time);
        var firstAdmin = Guid.CreateVersion7(Now);
        await handler.HandleAsync(new RevokeLicenseCommand(_license.Id, "Chargeback", firstAdmin), Token);
        _time.Advance(TimeSpan.FromHours(1));

        var again = await handler.HandleAsync(new RevokeLicenseCommand(_license.Id, "Other", Guid.CreateVersion7(Now)), Token);

        Assert.True(again.IsSuccess);
        Assert.Equal((Now, "Chargeback", firstAdmin), (_license.RevokedAt!.Value, _license.RevocationReason, _license.RevokedByAdminId));
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Revoke_ReturnsNotFoundAndValidatesTheReason()
    {
        var handler = new RevokeLicenseHandler(_store, _store, _time);

        var missing = await handler.HandleAsync(new RevokeLicenseCommand(Guid.CreateVersion7(), "Chargeback", Guid.CreateVersion7()), Token);
        var noReason = await handler.HandleAsync(new RevokeLicenseCommand(_license.Id, null, Guid.CreateVersion7()), Token);

        Assert.Same(LicenseErrors.NotFound, missing.Error);
        Assert.Equal(("license.invalid_revocation_reason", "reason"), (noReason.Error!.Code, noReason.Error.Field));
        Assert.Equal(LicenseStatus.Active, _license.Status);
    }

    [Fact]
    public async Task ReleaseActivation_RemovesItOnceThenAnswersNotFound()
    {
        _store.Activations.Add(Activation.Create(_license.Id, "srv-01", Now));
        var handler = new ReleaseActivationHandler(_store, _store, _store);

        var first = await handler.HandleAsync(new ReleaseActivationCommand(_license.Id, "srv-01"), Token);
        var second = await handler.HandleAsync(new ReleaseActivationCommand(_license.Id, "srv-01"), Token);
        var unknownLicense = await handler.HandleAsync(new ReleaseActivationCommand(Guid.CreateVersion7(), "srv-01"), Token);

        Assert.True(first.IsSuccess);
        Assert.Empty(_store.Activations);
        Assert.Same(LicenseErrors.ActivationNotFound, second.Error);
        Assert.Same(LicenseErrors.NotFound, unknownLicense.Error);
    }

    [Theory]
    [InlineData("revoked", 1, 20, LicenseStatus.Revoked)]
    [InlineData("EXPIRED", 3, 100, LicenseStatus.Expired)]
    [InlineData(null, null, null, null)]
    public async Task List_BuildsTheFilter(string? status, int? page, int? pageSize, LicenseStatus? expectedStatus)
    {
        var queries = new CapturingQueries();

        var result = await new ListLicensesHandler(queries, _time)
            .HandleAsync(new ListLicensesQuery(status, " FiveM-Garage ", page, pageSize), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new LicenseFilter(expectedStatus, "fivem-garage", page ?? 1, pageSize ?? ListLicensesHandler.DefaultPageSize, Now),
            queries.Filter);
    }

    [Theory]
    [InlineData("bogus", null, null, "request.invalid_status")]
    [InlineData("1", null, null, "request.invalid_status")]
    [InlineData("active,revoked", null, null, "request.invalid_status")]
    [InlineData("revoked, expired", null, null, "request.invalid_status")]
    [InlineData(null, 0, null, "request.invalid_page")]
    [InlineData(null, int.MaxValue, 100, "request.invalid_page")]
    [InlineData(null, ListLicensesHandler.MaxPage + 1, 1, "request.invalid_page")]
    [InlineData(null, null, 0, "request.invalid_page_size")]
    [InlineData(null, null, ListLicensesHandler.MaxPageSize + 1, "request.invalid_page_size")]
    public async Task List_RejectsInvalidParameters(string? status, int? page, int? pageSize, string expectedCode)
    {
        var queries = new CapturingQueries();

        var result = await new ListLicensesHandler(queries, _time).HandleAsync(new ListLicensesQuery(status, null, page, pageSize), Token);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Null(queries.Filter);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class CapturingQueries : IAdminQueries
    {
        public LicenseFilter? Filter { get; private set; }

        public Task<PagedResult<LicenseListItem>> ListLicensesAsync(LicenseFilter filter, CancellationToken cancellationToken)
        {
            Filter = filter;
            return Task.FromResult(new PagedResult<LicenseListItem>([], filter.Page, filter.PageSize, 0, 0));
        }

        public Task<IReadOnlyList<ProductView>> ListProductsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProductView?> GetProductAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LicenseDetails?> GetLicenseAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminStats> GetStatsAsync(DateTimeOffset now, int days, int topProducts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
