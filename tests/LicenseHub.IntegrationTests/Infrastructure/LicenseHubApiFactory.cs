using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LicenseHub.IntegrationTests.Infrastructure;

/// <summary>Runs the real API in memory against a MariaDB database from <see cref="MariaDbFixture"/>.</summary>
public sealed class LicenseHubApiFactory(string connectionString, string signingPrivateKey, int licensesPermitLimit)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:LicenseHub", connectionString);
        builder.UseSetting("Signing:PrivateKey", signingPrivateKey);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("RateLimiting:Licenses:PermitLimit", licensesPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:Default:PermitLimit", "100000");
    }
}
