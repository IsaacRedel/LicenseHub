using System.Net;
using LicenseHub.Api.Infrastructure;
using LicenseHub.Application.Licenses;
using LicenseHub.Infrastructure.Signing;

namespace LicenseHub.Api.Endpoints;

/// <param name="Timestamp">Client clock in Unix seconds; must be within 5 minutes of the server clock.</param>
public sealed record ValidateLicenseRequest(
    string? Key,
    string? ProductSlug,
    string? InstanceId,
    string? Nonce,
    long? Timestamp);

public sealed record DeactivateLicenseRequest(string? Key, string? InstanceId);

public sealed record SigningKeyResponse(string KeyId, string Algorithm, string PublicKeyPem);

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").WithTags("Licenses");

        v1.MapPost("/licenses/validate", ValidateAsync)
            .WithName("ValidateLicense")
            .WithSummary("Validates a license key and returns a signed verdict.")
            .WithDescription(
                "Business results (unknown key, revoked, expired, activation limit) return 200 with valid=false " +
                "and a reason, signed like a successful validation. The payload is base64url JSON; verify the " +
                "ES256 signature (IEEE P1363) over the decoded bytes with the public key embedded in the client " +
                "(also served by GET /v1/signing-key), then check that productSlug, keyFingerprint, nonce and " +
                "instanceId match the request before trusting valid.")
            .Produces<SignedResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimiting.LicensesPolicy);

        v1.MapPost("/licenses/deactivate", DeactivateAsync)
            .WithName("DeactivateLicense")
            .WithSummary("Frees the activation of an instance so another one can use the license.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimiting.LicensesPolicy);

        v1.MapGet("/signing-key", GetSigningKey)
            .WithName("GetSigningKey")
            .WithSummary("Returns the public key that verifies validation responses.")
            .Produces<SigningKeyResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> ValidateAsync(
        ValidateLicenseRequest request,
        HttpContext httpContext,
        ValidateLicenseHandler handler,
        ValidationResponseSigner signer,
        CancellationToken cancellationToken)
    {
        var command = new ValidateLicenseCommand(
            request.Key,
            request.ProductSlug,
            request.InstanceId,
            request.Nonce,
            request.Timestamp,
            ClientIp(httpContext));

        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(signer.Sign(result.Value))
            : result.Error!.ToProblem();
    }

    private static async Task<IResult> DeactivateAsync(
        DeactivateLicenseRequest request,
        DeactivateLicenseHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new DeactivateLicenseCommand(request.Key, request.InstanceId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static SigningKeyResponse GetSigningKey(ResponseSigningKey key) =>
        new(key.KeyId, ResponseSigningKey.Algorithm, key.PublicKeyPem);

    /// <summary>
    /// The connection address. It only reflects X-Forwarded-For when forwarded headers are enabled for
    /// configured proxies (see Program.cs).
    /// </summary>
    private static string? ClientIp(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString() is { Length: <= 45 } text
            ? text
            : IPAddress.None.ToString();
    }
}
