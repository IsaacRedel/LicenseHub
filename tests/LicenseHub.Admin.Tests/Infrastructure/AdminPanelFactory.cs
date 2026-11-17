using System.Text.RegularExpressions;
using LicenseHub.Admin.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests.Infrastructure;

/// <summary>Hosts the admin panel in memory, with the LicenseHub API replaced by <see cref="Api"/>.</summary>
public partial class AdminPanelFactory : WebApplicationFactory<Program>
{
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "licensehub-admin-tests", Guid.NewGuid().ToString("N"));

    public FakeApi Api { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("LicenseHub:ApiBaseUrl", "http://api.test/");
        builder.UseSetting("DataProtection:KeysPath", _keysPath);

        // Outside Development the framework files (_framework/blazor.web.js) are only found in a published app;
        // this maps them from the build output like `dotnet run` does.
        builder.UseStaticWebAssets();

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<LoginApiClient>().ConfigurePrimaryHttpMessageHandler(() => Api);
            services.AddHttpClient<AdminApiClient>().ConfigurePrimaryHttpMessageHandler(() => Api);
        });
    }

    /// <summary>
    /// A client on https://localhost, so the Secure session cookie is stored and sent back like a browser would on
    /// a secure context. Cookies are kept between requests; redirects are not followed.
    /// </summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    /// <summary>The antiforgery token rendered in a form.</summary>
    public static string AntiforgeryToken(string html) =>
        AntiforgeryTokenPattern().Match(html) is { Success: true } match
            ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The page has no antiforgery token.");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Api.Dispose();
        if (Directory.Exists(_keysPath))
        {
            Directory.Delete(_keysPath, recursive: true);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}
