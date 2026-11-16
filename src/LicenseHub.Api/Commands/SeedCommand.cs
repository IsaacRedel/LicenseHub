using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Licenses;
using LicenseHub.Application.Products;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Api.Commands;

/// <summary>
/// <c>seed</c> (Development only): applies migrations, creates the demo product if needed and issues a
/// new license, printing its key once. Lets the validation flow be tried before the admin endpoints exist.
/// </summary>
internal static class SeedCommand
{
    public const string Name = "seed";

    private const string DemoSlug = "demo";
    private const int DemoMaxActivations = 2;

    public static async Task<int> RunAsync(WebApplication app, TextWriter output, TextWriter error)
    {
        if (!app.Environment.IsDevelopment())
        {
            await error.WriteLineAsync("The seed command only runs in Development (ASPNETCORE_ENVIRONMENT=Development).");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();

        var product = await services.GetRequiredService<IProductRepository>().GetBySlugAsync(DemoSlug, CancellationToken.None);
        if (product is null)
        {
            var created = await services.GetRequiredService<CreateProductHandler>().HandleAsync(
                new CreateProductCommand("Demo Product", DemoSlug, DemoMaxActivations),
                CancellationToken.None);
            if (!created.IsSuccess)
            {
                await error.WriteLineAsync($"Could not create the demo product: {created.Error!.Message}");
                return 1;
            }

            await output.WriteLineAsync($"Created product '{DemoSlug}' ({DemoMaxActivations} activations).");
            product = await services.GetRequiredService<IProductRepository>().GetBySlugAsync(DemoSlug, CancellationToken.None);
        }

        var issued = await services.GetRequiredService<IssueLicenseHandler>().HandleAsync(
            new IssueLicenseCommand(product!.Id, "demo@example.com"),
            CancellationToken.None);
        if (!issued.IsSuccess)
        {
            await error.WriteLineAsync($"Could not issue the demo license: {issued.Error!.Message}");
            return 1;
        }

        var license = issued.Value;
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Product slug:    {product.Slug}");
        await output.WriteLineAsync($"License id:      {license.LicenseId}");
        await output.WriteLineAsync($"Max activations: {license.MaxActivations}");
        await output.WriteLineAsync($"License key:     {license.Key}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("The key is shown only now; only its SHA-256 hash is stored.");
        return 0;
    }
}
