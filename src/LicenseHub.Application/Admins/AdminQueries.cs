using LicenseHub.Domain.Licenses;

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
}
