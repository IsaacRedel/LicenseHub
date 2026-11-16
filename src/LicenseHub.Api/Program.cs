using LicenseHub.Api.Endpoints;
using LicenseHub.Api.Infrastructure;
using LicenseHub.Application;
using LicenseHub.Infrastructure;
using LicenseHub.Infrastructure.Persistence;
using LicenseHub.Infrastructure.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

const string DatabaseHealthCheck = "database";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Development defaults to throwing on unreadable bodies, which the exception handler would turn into a 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddResponseSigning(builder.Configuration, allowEphemeralKey: builder.Environment.IsDevelopment());
builder.Services.AddLicenseHubRateLimiting(builder.Configuration);
var useForwardedHeaders = builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
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

if (app.Services.GetRequiredService<ResponseSigningKey>().IsEphemeral)
{
    app.Logger.LogWarning(
        "Signing:PrivateKey is not configured; using an ephemeral signing key (keyId changes on every restart). " +
        "Run the 'keygen' command to create a persistent key.");
}

// Off by default; docker-compose.yml turns it on so `docker compose up` gives a ready database.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
}

// First, so the client IP seen by rate limiting and logs is the real one.
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// API reference only in Development: /openapi/v1.json and the Scalar UI at /scalar.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");
app.MapLicenseEndpoints();

await app.RunAsync();
