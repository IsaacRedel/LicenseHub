using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LicenseHub.Admin.Tests.Infrastructure;

/// <summary>Hosts the admin panel in memory for integration tests.</summary>
public class AdminPanelFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
