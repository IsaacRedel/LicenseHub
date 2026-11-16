using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Admins;
using LicenseHub.Infrastructure.Auth;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Signing;
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
        services.AddScoped<IActivationRepository, ActivationRepository>();
        services.AddScoped<IValidationLogRepository, ValidationLogRepository>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IAdminQueries, AdminQueries>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IAdminPasswordHasher, AdminPasswordHasher>();

        return services;
    }

    /// <summary>
    /// Registers the admin JWT settings (<c>AdminAuth:JwtKey</c>) and the token issuer. Without a key, an
    /// ephemeral one is generated only if <paramref name="allowEphemeralKey"/> is true (Development).
    /// </summary>
    public static AdminJwtSettings AddAdminTokens(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowEphemeralKey)
    {
        var settings = AdminJwtSettings.FromConfiguration(configuration, allowEphemeralKey);
        services.AddSingleton(settings);
        services.AddSingleton<IAdminTokenIssuer, JwtAdminTokenIssuer>();
        return settings;
    }

    public const string SigningKeySetting = "Signing:PrivateKey";

    /// <summary>
    /// Registers the response signing key from <c>Signing:PrivateKey</c>. When it is missing, an ephemeral
    /// key is generated only if <paramref name="allowEphemeralKey"/> is true (Development); otherwise
    /// startup fails.
    /// </summary>
    public static IServiceCollection AddResponseSigning(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowEphemeralKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredKey = configuration[SigningKeySetting];
        ResponseSigningKey key;
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            key = ResponseSigningKey.Load(configuredKey);
        }
        else if (allowEphemeralKey)
        {
            key = ResponseSigningKey.Generate(isEphemeral: true);
        }
        else
        {
            throw new InvalidOperationException(
                $"'{SigningKeySetting}' is not configured. Generate a key pair with the 'keygen' command " +
                "and set it with user-secrets or the Signing__PrivateKey environment variable.");
        }

        services.AddSingleton(key);
        services.AddSingleton<ValidationResponseSigner>();
        return services;
    }
}
