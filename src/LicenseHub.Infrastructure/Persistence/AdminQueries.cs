using LicenseHub.Application.Admins;
using LicenseHub.Domain.Licenses;
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

    public async Task<PagedResult<LicenseListItem>> ListLicensesAsync(LicenseFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var now = filter.Now;

        var rows = from license in db.Licenses.AsNoTracking()
                   join product in db.Products on license.ProductId equals product.Id
                   select new { License = license, product.Slug };

        if (filter.ProductSlug is { } slug)
        {
            rows = rows.Where(r => r.Slug == slug);
        }

        // Effective status, matching License.EffectiveStatus: an active license past its expiration is expired.
        rows = filter.Status switch
        {
            LicenseStatus.Active => rows.Where(r =>
                r.License.Status == LicenseStatus.Active && (r.License.ExpiresAt == null || r.License.ExpiresAt > now)),
            LicenseStatus.Revoked => rows.Where(r => r.License.Status == LicenseStatus.Revoked),
            LicenseStatus.Expired => rows.Where(r =>
                r.License.Status == LicenseStatus.Expired
                || (r.License.Status == LicenseStatus.Active && r.License.ExpiresAt != null && r.License.ExpiresAt <= now)),
            _ => rows,
        };

        var totalItems = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderByDescending(r => r.License.CreatedAt)
            .ThenByDescending(r => r.License.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new
            {
                r.License,
                r.Slug,
                ActiveActivations = db.Activations.Count(a => a.LicenseId == r.License.Id),
            })
            .ToListAsync(cancellationToken);

        var views = items
            .Select(r => new LicenseListItem(
                r.License.Id,
                r.License.ProductId,
                r.Slug,
                r.License.CustomerEmail,
                r.License.EffectiveStatus(now),
                r.License.MaxActivations,
                r.ActiveActivations,
                r.License.ExpiresAt,
                r.License.CreatedAt,
                r.License.RevokedAt))
            .ToList();

        var totalPages = (int)Math.Ceiling(totalItems / (double)filter.PageSize);
        return new PagedResult<LicenseListItem>(views, filter.Page, filter.PageSize, totalItems, totalPages);
    }

    public async Task<LicenseDetails?> GetLicenseAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await (from l in db.Licenses.AsNoTracking()
                         where l.Id == id
                         join p in db.Products on l.ProductId equals p.Id
                         select new { License = l, p.Slug })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var license = row.License;
        var activations = await db.Activations.AsNoTracking()
            .Where(a => a.LicenseId == id)
            .OrderBy(a => a.FirstValidatedAt)
            .ThenBy(a => a.Id)
            .Select(a => new ActivationView(a.InstanceId, a.FirstValidatedAt, a.LastValidatedAt))
            .ToListAsync(cancellationToken);

        RevocationView? revocation = null;
        if (license.RevokedAt is { } revokedAt)
        {
            var revokedBy = license.RevokedByAdminId is { } adminId
                ? await db.AdminUsers.AsNoTracking()
                    .Where(a => a.Id == adminId)
                    .Select(a => new AdminRef(a.Id, a.Email))
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            revocation = new RevocationView(revokedAt, license.RevocationReason, revokedBy);
        }

        return new LicenseDetails(
            license.Id,
            license.ProductId,
            row.Slug,
            license.CustomerEmail,
            license.EffectiveStatus(now),
            license.MaxActivations,
            activations.Count,
            license.ExpiresAt,
            license.CreatedAt,
            license.RevokedAt,
            Convert.ToHexString(license.KeyHash, 0, 4),
            revocation,
            activations);
    }

    private IQueryable<ProductView> ToViews(IQueryable<Product> products) =>
        products.AsNoTracking().Select(p => new ProductView(
            p.Id,
            p.Name,
            p.Slug,
            p.DefaultMaxActivations,
            p.CreatedAt,
            db.Licenses.Count(l => l.ProductId == p.Id)));
}
