using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LicenseHub.IntegrationTests.Infrastructure;

/// <summary>Runs the real API in memory against a MariaDB database from <see cref="MariaDbFixture"/>.</summary>
public sealed class LicenseHubApiFactory(IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
