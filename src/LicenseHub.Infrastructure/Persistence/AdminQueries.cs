using LicenseHub.Application.Admins;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;
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

    public async Task<AdminStats> GetStatsAsync(DateTimeOffset now, int days, int topProducts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(topProducts, 1);

        var licenses = db.Licenses.AsNoTracking();
        var byStatus = new LicenseStatusCounts(
            await licenses.CountAsync(
                l => l.Status == LicenseStatus.Active && (l.ExpiresAt == null || l.ExpiresAt > now),
                cancellationToken),
            await licenses.CountAsync(l => l.Status == LicenseStatus.Revoked, cancellationToken),
            await licenses.CountAsync(
                l => l.Status == LicenseStatus.Expired
                     || (l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt <= now),
                cancellationToken));

        var firstDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-(days - 1));
        var since = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // CreatedAt is stored in UTC, so DATE() gives the UTC day. Served by IX_validation_logs_CreatedAt_Outcome.
        var dailyRows = await db.Database
            .SqlQuery<DailyOutcomeRow>($"""
                SELECT DATE(CreatedAt) AS Day, Outcome, COUNT(*) AS Total
                FROM validation_logs
                WHERE CreatedAt >= {since}
                GROUP BY DATE(CreatedAt), Outcome
                """)
            .ToListAsync(cancellationToken);

        var perDay = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day =>
            {
                int Count(ValidationOutcome outcome) => (int)dailyRows
                    .Where(r => DateOnly.FromDateTime(r.Day) == day && r.Outcome == outcome.ToString())
                    .Sum(r => r.Total);

                return new DailyValidations(
                    day,
                    Count(ValidationOutcome.Valid),
                    Count(ValidationOutcome.UnknownKey),
                    Count(ValidationOutcome.ProductMismatch),
                    Count(ValidationOutcome.Revoked),
                    Count(ValidationOutcome.Expired),
                    Count(ValidationOutcome.ActivationLimitReached));
            })
            .ToList();

        var top = await (from log in db.ValidationLogs.AsNoTracking()
                         where log.CreatedAt >= since && log.LicenseId != null
                         join license in db.Licenses on log.LicenseId equals license.Id
                         join product in db.Products on license.ProductId equals product.Id
                         group product by new { product.Id, product.Slug, product.Name } into g
                         orderby g.Count() descending, g.Key.Name
                         select new { g.Key.Id, g.Key.Slug, g.Key.Name, Validations = g.Count() })
            .Take(topProducts)
            .ToListAsync(cancellationToken);

        return new AdminStats(
            byStatus,
            perDay,
            top.Select(p => new ProductValidations(p.Id, p.Slug, p.Name, p.Validations)).ToList());
    }

    private sealed record DailyOutcomeRow(DateTime Day, string Outcome, long Total);

    private IQueryable<ProductView> ToViews(IQueryable<Product> products) =>
        products.AsNoTracking().Select(p => new ProductView(
            p.Id,
            p.Name,
            p.Slug,
            p.DefaultMaxActivations,
            p.CreatedAt,
            db.Licenses.Count(l => l.ProductId == p.Id)));
}
