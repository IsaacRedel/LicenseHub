using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.Application.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Expects an already normalized slug (see <see cref="Product.NormalizeSlug"/>).</summary>
    Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Expects an already normalized slug (see <see cref="Product.NormalizeSlug"/>).</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void Add(Product product);
}

public interface ILicenseRepository
{
    /// <summary>Read-only lookup by key hash. The result is not tracked for changes.</summary>
    Task<License?> FindByKeyHashAsync(byte[] keyHash, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the license and locks its row until the current transaction ends, so concurrent
    /// activations of the same license run one at a time. Must run inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<License?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(License license);
}

public interface IActivationRepository
{
    Task<IReadOnlyList<Activation>> ListByLicenseAsync(Guid licenseId, CancellationToken cancellationToken);

    /// <summary>Expects an already normalized instance id (see <see cref="Activation.NormalizeInstanceId"/>).</summary>
    Task<Activation?> FindAsync(Guid licenseId, string instanceId, CancellationToken cancellationToken);

    void Add(Activation activation);

    void Remove(Activation activation);
}

public interface IValidationLogRepository
{
    void Add(ValidationLog log);
}

public interface IUnitOfWork
{
    /// <exception cref="UniqueConstraintException">A unique index rejected the changes.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> in a database transaction and commits it. The operation may be
    /// retried after a transient failure (such as a deadlock), so it must load everything it needs
    /// itself and must not rely on entities loaded before the call.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when the database rejects a duplicate value,
/// so use cases can turn races on unique fields into a normal error.
/// </summary>
public sealed class UniqueConstraintException(string message, Exception? innerException = null)
    : Exception(message, innerException);
