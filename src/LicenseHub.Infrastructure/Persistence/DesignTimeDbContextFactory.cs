using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LicenseHub.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tools. Generating migrations does not open a connection,
/// so the connection string below is a placeholder without credentials.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LicenseHubDbContext>
{
    public LicenseHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LicenseHub")
            ?? "Server=localhost;Database=licensehub";

        var options = new DbContextOptionsBuilder<LicenseHubDbContext>()
            .UseMySql(connectionString, LicenseHubDbContext.ServerVersion)
            .Options;

        return new LicenseHubDbContext(options);
    }
}
