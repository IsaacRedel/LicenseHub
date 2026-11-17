using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseHub.Api.Commands;
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

// `keygen` needs neither configuration nor a database.
if (args is [KeygenCommand.Name, ..])
{
    return KeygenCommand.Run(Console.Out);
}

var builder = WebApplication.CreateBuilder(args);

// Commands print their own result; keep framework and SQL logs out of the way. Added last, so these values
// override the Logging section of appsettings.
if (args is [SeedCommand.Name, ..] or [AdminCommand.Name, ..])
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
    });
}

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // Use-case errors already carry a code; a 400 without one comes from a body the binder could not read.
    if (context.ProblemDetails.Status == StatusCodes.Status400BadRequest
        && !context.ProblemDetails.Extensions.ContainsKey("code"))
    {
        context.ProblemDetails.Extensions["code"] = "request.malformed_body";
        context.ProblemDetails.Detail ??= "The request body is missing, is not valid JSON or has fields of the wrong type.";
    }

    // Bearer challenges (no token, expired, wrong signature) leave an empty 401.
    if (context.ProblemDetails.Status == StatusCodes.Status401Unauthorized
        && !context.ProblemDetails.Extensions.ContainsKey("code"))
    {
        context.ProblemDetails.Extensions["code"] = "auth.unauthorized";
        context.ProblemDetails.Detail ??= "A valid admin bearer token is required.";
    }
});
builder.Services.AddLicenseHubOpenApi();

// Enums as camelCase strings ("active", "revoked") in admin responses.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Development defaults to throwing on unreadable bodies, which the exception handler would turn into a 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddResponseSigning(builder.Configuration, allowEphemeralKey: builder.Environment.IsDevelopment());
var adminJwt = builder.Services.AddAdminTokens(builder.Configuration, allowEphemeralKey: builder.Environment.IsDevelopment());
builder.Services.AddAdminAuthentication(adminJwt);
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

if (args is [SeedCommand.Name, ..])
{
    return await SeedCommand.RunAsync(app, Console.Out, Console.Error);
}

if (args is [AdminCommand.Name, ..])
{
    return await AdminCommand.RunAsync(app, args, Console.In, Console.Out, Console.Error);
}

if (app.Services.GetRequiredService<ResponseSigningKey>().IsEphemeral)
{
    app.Logger.LogWarning(
        "Signing:PrivateKey is not configured; using an ephemeral signing key (keyId changes on every restart). " +
        "Run the 'keygen' command to create a persistent key.");
}

if (adminJwt.IsEphemeral)
{
    app.Logger.LogWarning(
        "AdminAuth:JwtKey is not configured; using an ephemeral key, so admin tokens stop working on restart. " +
        "Run the 'keygen' command to create a persistent key.");
}

// Off by default; docker-compose.yml turns it on so `docker compose up` gives a ready database.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<LicenseHubDbContext>().Database.MigrateAsync();
    }
}

// First, so the client IP seen by rate limiting and logs is the real one.
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
// Authentication before the rate limiter, so admin calls are limited per admin rather than per panel IP.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// API reference only in Development: /openapi/v1.json and the Scalar UI at /scalar.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.AddPreferredSecuritySchemes(OpenApiSetup.BearerScheme));
}

app.MapHealthChecks("/health");
app.MapLicenseEndpoints();
app.MapAdminEndpoints()
    .MapAdminProducts()
    .MapAdminLicenses()
    .MapAdminStats();

await app.RunAsync();
return 0;
