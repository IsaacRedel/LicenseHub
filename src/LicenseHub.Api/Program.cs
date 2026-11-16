using LicenseHub.Application;
using LicenseHub.Infrastructure;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

const string DatabaseHealthCheck = "database";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<LicenseHubDbContext>(DatabaseHealthCheck);

// CanConnectAsync goes through the retry strategy, so a down database would hang /health for
// minutes. The timeout makes the check report Unhealthy quickly instead.
builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
{
    foreach (var registration in options.Registrations.Where(r => r.Name == DatabaseHealthCheck))
    {
        registration.Timeout = TimeSpan.FromSeconds(5);
    }
});

var app = builder.Build();

// Off by default; docker-compose.yml turns it on so `docker compose up` gives a ready database.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/health");

await app.RunAsync();
