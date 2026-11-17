using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Validation;

namespace LicenseHub.Application.Admins;

public sealed record ProductView(
    Guid Id,
    string Name,
    string Slug,
    int DefaultMaxActivations,
    DateTimeOffset CreatedAt,
    int LicenseCount);

/// <param name="Status">Effective status: an active license past its expiration is reported as expired.</param>
public sealed record LicenseListItem(
    Guid Id,
    Guid ProductId,
    string ProductSlug,
    string CustomerEmail,
    LicenseStatus Status,
    int MaxActivations,
    int ActiveActivations,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record ActivationView(string InstanceId, DateTimeOffset FirstValidatedAt, DateTimeOffset LastValidatedAt);

public sealed record AdminRef(Guid Id, string Email);

/// <param name="RevokedBy">Null when revoked by the system or when the admin was removed.</param>
public sealed record RevocationView(DateTimeOffset RevokedAt, string? Reason, AdminRef? RevokedBy);

/// <summary>License details for the admin API. Never carries the key or the full key hash.</summary>
/// <param name="KeyHashPrefix">First 8 hex characters of the key hash, so support can match a customer's key.</param>
public sealed record LicenseDetails(
    Guid Id,
    Guid ProductId,
    string ProductSlug,
    string CustomerEmail,
    LicenseStatus Status,
    int MaxActivations,
    int ActiveActivations,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    string KeyHashPrefix,
    RevocationView? Revocation,
    IReadOnlyList<ActivationView> Activations);

public sealed record LicenseStatusCounts(int Active, int Revoked, int Expired);

/// <summary>Validations of one UTC day, by outcome.</summary>
public sealed record DailyValidations(
    DateOnly Date,
    int Valid,
    int UnknownKey,
    int ProductMismatch,
    int Revoked,
    int Expired,
    int ActivationLimitReached);

/// <param name="Validations">Validations of this product's licenses in the period (unknown keys have no product).</param>
public sealed record ProductValidations(Guid ProductId, string Slug, string Name, int Validations);

/// <param name="LicensesByStatus">Effective status, like the license list.</param>
/// <param name="ValidationsPerDay">Every day of the period in order, including days without validations.</param>
public sealed record AdminStats(
    LicenseStatusCounts LicensesByStatus,
    IReadOnlyList<DailyValidations> ValidationsPerDay,
    IReadOnlyList<ProductValidations> TopProducts);

public sealed record OverviewKpis(int ActiveLicenses, int RevokedLicenses, int ExpiredLicenses, int ValidationsToday);

/// <summary>Today's validations (in the requested time zone), accepted and refused by reason.</summary>
public sealed record TodayValidations(
    DateOnly Date,
    int Total,
    int Accepted,
    int UnknownKey,
    int ProductMismatch,
    int Revoked,
    int Expired,
    int ActivationLimitReached);

public sealed record DayValidations(DateOnly Date, int Accepted, int Refused);

/// <param name="ActivationsUsed">Activations of the product's active licenses.</param>
/// <param name="ActivationCapacity">Sum of the activation limits of the product's active licenses.</param>
public sealed record ProductUsage(
    Guid ProductId,
    string Name,
    string Slug,
    int ActiveLicenses,
    int ActivationsUsed,
    int ActivationCapacity);

/// <summary>Everything the admin dashboard shows, aggregated in the database.</summary>
public sealed record StatsOverview(
    OverviewKpis Kpis,
    TodayValidations Today,
    IReadOnlyList<DayValidations> Last30Days,
    IReadOnlyList<ProductUsage> Products);

/// <param name="ProductSlug">Null when the key matched no license.</param>
public sealed record ValidationLogView(
    Guid Id,
    DateTimeOffset CreatedAt,
    ValidationOutcome Outcome,
    Guid? LicenseId,
    string? ProductSlug,
    string? IpAddress);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems, int TotalPages);

/// <param name="Status">Effective status to filter by; null for any.</param>
/// <param name="ProductSlug">Normalized slug; null for any product.</param>
/// <param name="Now">Reference time for the effective status.</param>
public sealed record LicenseFilter(LicenseStatus? Status, string? ProductSlug, int Page, int PageSize, DateTimeOffset Now);

/// <summary>Read-side queries for the admin API, implemented with database projections.</summary>
public interface IAdminQueries
{
    /// <summary>All products ordered by name.</summary>
    Task<IReadOnlyList<ProductView>> ListProductsAsync(CancellationToken cancellationToken);

    Task<ProductView?> GetProductAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first, with the id as a stable tie-breaker.</summary>
    Task<PagedResult<LicenseListItem>> ListLicensesAsync(LicenseFilter filter, CancellationToken cancellationToken);

    Task<LicenseDetails?> GetLicenseAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// License counts by effective status, validations per UTC day over the last <paramref name="days"/> days
    /// (today included) and the <paramref name="topProducts"/> products with most validations in that period.
    /// </summary>
    Task<AdminStats> GetStatsAsync(DateTimeOffset now, int days, int topProducts, CancellationToken cancellationToken);

    /// <summary>
    /// Dashboard data. Days are calendar days at <paramref name="utcOffsetMinutes"/> from UTC; the last 30 days
    /// include today and days without validations.
    /// </summary>
    Task<StatsOverview> GetOverviewAsync(DateTimeOffset now, int utcOffsetMinutes, CancellationToken cancellationToken);

    /// <summary>Most recent validation attempts, newest first, optionally for one license.</summary>
    Task<IReadOnlyList<ValidationLogView>> ListValidationsAsync(int limit, Guid? licenseId, CancellationToken cancellationToken);
}
