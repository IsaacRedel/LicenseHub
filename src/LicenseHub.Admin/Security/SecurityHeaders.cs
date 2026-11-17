namespace LicenseHub.Admin.Security;

/// <summary>
/// Adds the security headers to every response, including static files, redirects and errors.
/// The Content-Security-Policy allows only same-origin resources: no inline scripts or style attributes, so the
/// panel never uses style="" (sizes go in SVG attributes) and loads its scripts from files.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;
                return Task.CompletedTask;
            });

            await next(context);
        });
}
