using LicenseHub.Application.Admins;
using LicenseHub.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Persistence;

/// <summary>
/// Filters and ordering are applied to the entities before projecting: EF cannot translate member access on a
/// type built through its constructor.
/// </summary>
internal sealed class AdminQueries(LicenseHubDbContext db) : IAdminQueries
{
    public async Task<IReadOnlyList<ProductView>> ListProductsAsync(CancellationToken cancellationToken) =>
        await ToViews(db.Products.OrderBy(p => p.Name).ThenBy(p => p.Id)).ToListAsync(cancellationToken);

    public Task<ProductView?> GetProductAsync(Guid id, CancellationToken cancellationToken) =>
        ToViews(db.Products.Where(p => p.Id == id)).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<ProductView> ToViews(IQueryable<Product> products) =>
        products.AsNoTracking().Select(p => new ProductView(
            p.Id,
            p.Name,
            p.Slug,
            p.DefaultMaxActivations,
            p.CreatedAt,
            db.Licenses.Count(l => l.ProductId == p.Id)));
}
