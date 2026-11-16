using LicenseHub.Application;
using LicenseHub.Infrastructure;
using LicenseHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddHealthChecks().AddDbContextCheck<LicenseHubDbContext>("database");

var app = builder.Build();

// Off by default; docker-compose.yml turns it on so `docker compose up` gives a ready database.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/health");

await app.RunAsync();
