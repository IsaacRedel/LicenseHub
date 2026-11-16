using LicenseHub.Application.Admins;
using LicenseHub.Application.Licenses;
using LicenseHub.Application.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LicenseHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<IssueLicenseHandler>();
        services.AddScoped<ValidateLicenseHandler>();
        services.AddScoped<DeactivateLicenseHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<CreateAdminHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<ListLicensesHandler>();
        services.AddScoped<RevokeLicenseHandler>();
        services.AddScoped<ReleaseActivationHandler>();
        return services;
    }
}
