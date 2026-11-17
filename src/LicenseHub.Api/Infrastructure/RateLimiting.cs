using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace LicenseHub.Api.Infrastructure;

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Bound from the "RateLimiting" configuration section.</summary>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Validate and deactivate: strict, to slow down key guessing.</summary>
    public RateLimitRule Licenses { get; set; } = new() { PermitLimit = 20 };

    /// <summary>POST /admin/auth/login: the strictest, on top of the per-account lockout.</summary>
    public RateLimitRule Login { get; set; } = new() { PermitLimit = 5 };

    /// <summary>Every other endpoint except /health.</summary>
    public RateLimitRule Default { get; set; } = new() { PermitLimit = 100 };
}

public static class RateLimiting
{
    /// <summary>Policy for POST /v1/licenses/validate and /deactivate.</summary>
    public const string LicensesPolicy = "licenses";

    /// <summary>Policy for POST /admin/auth/login.</summary>
    public const string LoginPolicy = "login";

    private const int LicensesSegments = 6;

    public static IServiceCollection AddLicenseHubRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>() ?? new RateLimitingSettings();
        Validate(settings.Licenses, nameof(settings.Licenses));
        Validate(settings.Login, nameof(settings.Login));
        Validate(settings.Default, nameof(settings.Default));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, cancellationToken) => WriteRejectionAsync(context, settings, cancellationToken);

            // Partitioned by client IP, except authenticated admin calls, which are partitioned by admin: they all
            // arrive from the admin panel's address. Behind a reverse proxy the IP is only the real client when the
            // proxy is listed in ForwardedHeaders; otherwise every caller shares the proxy's address.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(DefaultPartitionKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.Default.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.Default.WindowSeconds),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(LicensesPolicy, context =>
                RateLimitPartition.GetSlidingWindowLimiter(ClientKey(context), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = settings.Licenses.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.Licenses.WindowSeconds),
                    SegmentsPerWindow = LicensesSegments,
                    QueueLimit = 0,
                }));

            options.AddPolicy(LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.Login.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.Login.WindowSeconds),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    private static string ClientKey(HttpContext context) => PartitionKey(context.Connection.RemoteIpAddress);

    /// <summary>
    /// "admin:{sub}" for a request already authenticated with an admin token (authentication runs before the rate
    /// limiter), otherwise the client IP partition.
    /// </summary>
    internal static string DefaultPartitionKey(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/admin")
        && context.User.Identity?.IsAuthenticated == true
        && context.User.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value is { Length: > 0 } sub
            ? $"admin:{sub}"
            : ClientKey(context);

    /// <summary>
    /// The rate limit partition for an address. IPv6 clients usually control a whole /64, so they are grouped
    /// by that prefix; otherwise each address in it would get its own budget. IPv4-mapped IPv6 counts as IPv4.
    /// </summary>
    public static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext context,
        RateLimitingSettings settings,
        CancellationToken cancellationToken)
    {
        // Fixed windows report when the window resets; sliding windows do not, so fall back to one segment.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter.TotalSeconds
            : IsLicensesEndpoint(context.HttpContext)
                ? (double)settings.Licenses.WindowSeconds / LicensesSegments
                : settings.Default.WindowSeconds;

        context.HttpContext.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(retryAfterSeconds)).ToString(CultureInfo.InvariantCulture);

        var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Rate limit exceeded. Retry after the number of seconds in the Retry-After header.",
                Extensions = { ["code"] = "rate_limited" },
            },
        });
    }

    private static bool IsLicensesEndpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == LicensesPolicy;

    private static void Validate(RateLimitRule rule, string name)
    {
        if (rule.PermitLimit < 1 || rule.WindowSeconds < 1)
        {
            throw new InvalidOperationException(
                $"RateLimiting:{name} needs PermitLimit and WindowSeconds greater than zero.");
        }
    }
}
