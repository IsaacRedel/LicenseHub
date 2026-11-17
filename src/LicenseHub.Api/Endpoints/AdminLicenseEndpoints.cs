using System.Globalization;
using LicenseHub.Api.Infrastructure;
using LicenseHub.Application.Admins;
using LicenseHub.Application.Common;
using LicenseHub.Application.Licenses;

namespace LicenseHub.Api.Endpoints;

/// <param name="MaxActivations">Null uses the product default.</param>
/// <param name="ExpiresAt">Null issues a license that never expires.</param>
public sealed record IssueLicenseRequest(Guid? ProductId, string? CustomerEmail, int? MaxActivations, DateTimeOffset? ExpiresAt);

/// <summary>The only response that carries the license key; it cannot be retrieved again.</summary>
public sealed record IssuedLicenseResponse(
    Guid Id,
    Guid ProductId,
    string ProductSlug,
    string Key,
    string CustomerEmail,
    int MaxActivations,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt)
{
    // Records print every property by default; keep the key out of logs.
    public override string ToString() => $"IssuedLicenseResponse {{ Id = {Id}, Key = LH-****-****-****-**** }}";
}

public sealed record RevokeLicenseRequest(string? Reason);

public static class AdminLicenseEndpoints
{
    public static RouteGroupBuilder MapAdminLicenses(this RouteGroupBuilder admin)
    {
        var licenses = admin.MapGroup("/licenses").WithTags("Admin: licenses");

        licenses.MapPost("", IssueAsync)
            .WithName("AdminIssueLicense")
            .WithSummary("Issues a license. The key appears only in this response.")
            .Produces<IssuedLicenseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        licenses.MapGet("", ListAsync)
            .WithName("AdminListLicenses")
            .WithSummary("Lists licenses, newest first, filtered by effective status and product.")
            .WithDescription(
                "status: active, revoked or expired (an active license past its expiration counts as expired). " +
                $"page starts at 1; pageSize defaults to {ListLicensesHandler.DefaultPageSize} and is at most " +
                $"{ListLicensesHandler.MaxPageSize}.")
            .Produces<PagedResult<LicenseListItem>>()
            .ProducesValidationProblem();

        licenses.MapGet("/{id:guid}", GetAsync)
            .WithName("AdminGetLicense")
            .WithSummary("License details with its activations. Never returns the key or the full key hash.")
            .Produces<LicenseDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        licenses.MapPost("/{id:guid}/revoke", RevokeAsync)
            .WithName("AdminRevokeLicense")
            .WithSummary("Revokes a license. Idempotent: a second call keeps the original date, reason and admin.")
            .Produces<LicenseDetails>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Catch-all: instance ids may contain "/" (sent literally or as %2F).
        licenses.MapDelete("/{id:guid}/activations/{**instanceId}", ReleaseActivationAsync)
            .WithName("AdminReleaseActivation")
            .WithSummary("Frees an activation slot so another instance can use the license.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return admin;
    }

    private static async Task<IResult> IssueAsync(
        IssueLicenseRequest request,
        IssueLicenseHandler handler,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (request.ProductId is not { } productId)
        {
            return new Error("request.invalid_product_id", "Product id is required.", Field: "productId").ToProblem();
        }

        var result = await handler.HandleAsync(
            new IssueLicenseCommand(productId, request.CustomerEmail ?? string.Empty, request.MaxActivations, request.ExpiresAt),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var issued = result.Value;
        var product = await queries.GetProductAsync(issued.ProductId, cancellationToken);
        var response = new IssuedLicenseResponse(
            issued.LicenseId,
            issued.ProductId,
            product!.Slug,
            issued.Key,
            issued.CustomerEmail,
            issued.MaxActivations,
            issued.ExpiresAt,
            issued.CreatedAt);

        return TypedResults.Created($"/admin/licenses/{issued.LicenseId}", response);
    }

    private static async Task<IResult> ListAsync(
        string? status,
        string? productSlug,
        string? page,
        string? pageSize,
        ListLicensesHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalInt(page, out var pageNumber))
        {
            return new Error("request.invalid_page", "Page must be a whole number.", Field: "page").ToProblem();
        }

        if (!TryParseOptionalInt(pageSize, out var size))
        {
            return new Error("request.invalid_page_size", "Page size must be a whole number.", Field: "pageSize").ToProblem();
        }

        var result = await handler.HandleAsync(new ListLicensesQuery(status, productSlug, pageNumber, size), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IAdminQueries queries,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        await queries.GetLicenseAsync(id, timeProvider.GetUtcNow(), cancellationToken) is { } details
            ? TypedResults.Ok(details)
            : LicenseErrors.NotFound.ToProblem();

    private static async Task<IResult> RevokeAsync(
        Guid id,
        RevokeLicenseRequest request,
        HttpContext httpContext,
        RevokeLicenseHandler handler,
        IAdminQueries queries,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RevokeLicenseCommand(id, request.Reason, httpContext.User.GetAdminId()),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        return TypedResults.Ok(await queries.GetLicenseAsync(id, timeProvider.GetUtcNow(), cancellationToken));
    }

    private static async Task<IResult> ReleaseActivationAsync(
        Guid id,
        string instanceId,
        ReleaseActivationHandler handler,
        CancellationToken cancellationToken)
    {
        // Kestrel decodes every escape in the path except %2F, which would never match the stored id.
        var decoded = instanceId.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

        var result = await handler.HandleAsync(new ReleaseActivationCommand(id, decoded), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    private static bool TryParseOptionalInt(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
