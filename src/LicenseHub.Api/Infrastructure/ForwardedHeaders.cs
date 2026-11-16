using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace LicenseHub.Api.Infrastructure;

/// <summary>
/// X-Forwarded-For is honored only from proxies listed in configuration:
/// <c>ForwardedHeaders:KnownProxies</c> (IP addresses) and <c>ForwardedHeaders:KnownNetworks</c> (CIDR).
/// With none configured the middleware is not added, so clients cannot spoof their IP to dodge rate limits.
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
