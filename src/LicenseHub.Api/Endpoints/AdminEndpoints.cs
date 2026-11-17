using LicenseHub.Api.Infrastructure;
using LicenseHub.Application.Admins;

namespace LicenseHub.Api.Endpoints;

public sealed record AdminLoginRequest(string? Email, string? Password);

public sealed record AdminLoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public static class AdminEndpoints
{
    /// <summary>
    /// Maps /admin. Every route requires an admin token (<see cref="AdminAuthentication.AdminPolicy"/>) except
    /// the login.
    /// </summary>
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin")
            .RequireAuthorization(AdminAuthentication.AdminPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        admin.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.LoginPolicy)
            .WithTags("Admin: auth")
            .WithName("AdminLogin")
            .WithSummary("Exchanges an admin email and password for a 30-minute bearer token.")
            .WithDescription(
                "Wrong password, unknown email and locked-out account all return the same 401, so the response " +
                "does not reveal which accounts exist. Five consecutive failures lock the account for 15 minutes.")
            .Produces<AdminLoginResponse>()
            .ProducesValidationProblem();

        return admin;
    }

    private static async Task<IResult> LoginAsync(
        AdminLoginRequest request,
        HttpContext httpContext,
        LoginHandler handler,
        CancellationToken cancellationToken)
    {
        // The response carries a bearer token: no cache may keep it (RFC 6749, section 5.1).
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(new AdminLoginResponse(result.Value.AccessToken, "Bearer", result.Value.ExpiresAt))
            : result.Error!.ToProblem();
    }
}
