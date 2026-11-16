using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;

namespace LicenseHub.Application.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Expects an already normalized slug (see <see cref="Product.NormalizeSlug"/>).</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void Add(Product product);
}

public interface ILicenseRepository
{
    void Add(License license);
}

public interface IUnitOfWork
{
    /// <exception cref="UniqueConstraintException">A unique index rejected the changes.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when the database rejects a duplicate value,
/// so use cases can turn races on unique fields into a normal error.
/// </summary>
public sealed class UniqueConstraintException(string message, Exception? innerException = null)
    : Exception(message, innerException);
