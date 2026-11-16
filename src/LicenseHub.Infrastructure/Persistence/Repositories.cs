using System.Data;
using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace LicenseHub.Infrastructure.Persistence;

internal sealed class ProductRepository(LicenseHubDbContext db) : IProductRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.FindAsync([id], cancellationToken);

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(p => p.Slug == slug, cancellationToken);

    public void Add(Product product) => db.Products.Add(product);
}

internal sealed class LicenseRepository(LicenseHubDbContext db) : ILicenseRepository
{
    public Task<License?> FindByKeyHashAsync(byte[] keyHash, CancellationToken cancellationToken) =>
        db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.KeyHash == keyHash, cancellationToken);

    public async Task<License?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("GetForUpdateAsync must run inside a transaction.");
        }

        // FOR UPDATE holds an exclusive lock on the row until commit. ToListAsync keeps EF from wrapping
        // the statement in a subquery, where the locking clause would not apply.
        var rows = await db.Licenses
            .FromSql($"SELECT * FROM licenses WHERE Id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public void Add(License license) => db.Licenses.Add(license);
}

internal sealed class ActivationRepository(LicenseHubDbContext db) : IActivationRepository
{
    public async Task<IReadOnlyList<Activation>> ListByLicenseAsync(Guid licenseId, CancellationToken cancellationToken) =>
        await db.Activations.Where(a => a.LicenseId == licenseId).ToListAsync(cancellationToken);

    public Task<Activation?> FindAsync(Guid licenseId, string instanceId, CancellationToken cancellationToken) =>
        db.Activations.FirstOrDefaultAsync(a => a.LicenseId == licenseId && a.InstanceId == instanceId, cancellationToken);

    public void Add(Activation activation) => db.Activations.Add(activation);

    public void Remove(Activation activation) => db.Activations.Remove(activation);
}

internal sealed class ValidationLogRepository(LicenseHubDbContext db) : IValidationLogRepository
{
    public void Add(ValidationLog log) => db.ValidationLogs.Add(log);
}

internal sealed class EfUnitOfWork(LicenseHubDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // Detach the rejected entries so the context can be reused in the same scope.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new UniqueConstraintException("A unique index rejected the change.", ex);
        }
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // The retry strategy replays the whole block after a transient failure such as a deadlock (1213),
        // so every attempt starts with an empty change tracker and a fresh transaction.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();

                // READ COMMITTED: reads after the row lock see what the previous lock holder committed.
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            cancellationToken);
    }
}
