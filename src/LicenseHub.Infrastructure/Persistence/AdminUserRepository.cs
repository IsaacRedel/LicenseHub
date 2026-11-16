using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Admins;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Persistence;

internal sealed class AdminUserRepository(LicenseHubDbContext db) : IAdminUserRepository
{
    public Task<AdminUser?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(a => a.Email == email, cancellationToken);

    public Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<AdminUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("GetForUpdateAsync must run inside a transaction.");
        }

        var rows = await db.AdminUsers
            .FromSql($"SELECT * FROM admin_users WHERE Id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public void Add(AdminUser admin) => db.AdminUsers.Add(admin);
}
