using LicenseHub.Domain.Admins;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Payments;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class LicenseHubDbContext(DbContextOptions<LicenseHubDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Fixed instead of auto-detected so migrations can be generated without a running database.
    /// Matches the image in docker-compose.yml.
    /// </summary>
    public static readonly ServerVersion ServerVersion = new MariaDbServerVersion(new Version(11, 4));

    public DbSet<Product> Products => Set<Product>();

    public DbSet<License> Licenses => Set<License>();

    public DbSet<Activation> Activations => Set<Activation>();

    public DbSet<ValidationLog> ValidationLogs => Set<ValidationLog>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LicenseHubDbContext).Assembly);
    }
}
