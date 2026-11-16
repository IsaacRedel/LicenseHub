namespace LicenseHub.Application.Admins;

public sealed record ProductView(
    Guid Id,
    string Name,
    string Slug,
    int DefaultMaxActivations,
    DateTimeOffset CreatedAt,
    int LicenseCount);

/// <summary>Read-side queries for the admin API, implemented with database projections.</summary>
public interface IAdminQueries
{
    /// <summary>All products ordered by name.</summary>
    Task<IReadOnlyList<ProductView>> ListProductsAsync(CancellationToken cancellationToken);

    Task<ProductView?> GetProductAsync(Guid id, CancellationToken cancellationToken);
}
