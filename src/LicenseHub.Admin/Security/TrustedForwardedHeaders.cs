using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace LicenseHub.Admin.Security;

/// <summary>
/// For a panel behind a reverse proxy: X-Forwarded-For and X-Forwarded-Proto are honored only from the proxies in
/// <c>ForwardedHeaders:KnownProxies</c> (IP addresses) and <c>ForwardedHeaders:KnownNetworks</c> (CIDR). The login
/// forwards the browser's address to the API, so without this every admin would share the proxy's login rate limit.
/// With none configured the middleware is not added, so browsers cannot spoof their address. Same rules as the API.
/// </summary>
public static class TrustedForwardedHeaders
{
    public const string SectionName = "ForwardedHeaders";

    public static bool AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
        {
            return false;
        }

        var parsedProxies = proxies.Select(p => IPAddress.TryParse(p, out var ip)
            ? ip
            : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies has an invalid IP address: '{p}'.")).ToList();
        var parsedNetworks = networks.Select(n => System.Net.IPNetwork.TryParse(n, out var network)
            ? network
            : throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks has an invalid CIDR: '{n}'.")).ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // Drop the loopback defaults: trust exactly what is configured.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            parsedProxies.ForEach(options.KnownProxies.Add);
            parsedNetworks.ForEach(options.KnownIPNetworks.Add);
        });

        return true;
    }
}
