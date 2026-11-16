using LicenseHub.Application.Abstractions;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "LicenseHub";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail at startup: the API cannot do anything useful without its database.
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "Set it with user-secrets or the ConnectionStrings__LicenseHub environment variable.");
        }

        services.AddDbContext<LicenseHubDbContext>(options => options.UseMySql(
            connectionString,
            LicenseHubDbContext.ServerVersion,
            mySql => mySql.EnableRetryOnFailure()));

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
