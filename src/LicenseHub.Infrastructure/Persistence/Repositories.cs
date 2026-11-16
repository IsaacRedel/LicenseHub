using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace LicenseHub.Infrastructure.Persistence;

internal sealed class ProductRepository(LicenseHubDbContext db) : IProductRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Products.FindAsync([id], cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(p => p.Slug == slug, cancellationToken);

    public void Add(Product product) => db.Products.Add(product);
}

internal sealed class LicenseRepository(LicenseHubDbContext db) : ILicenseRepository
{
    public void Add(License license) => db.Licenses.Add(license);
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
}
