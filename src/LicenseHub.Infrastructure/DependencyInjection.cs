using LicenseHub.Application.Abstractions;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "LicenseHub";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // The connection string is resolved when the first DbContext is created, so the API still
        // starts (and /health reports Unhealthy) when the database is not configured.
        services.AddDbContext<LicenseHubDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
            }

            options.UseMySql(
                connectionString,
                LicenseHubDbContext.ServerVersion,
                mySql => mySql.EnableRetryOnFailure());
        });

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
