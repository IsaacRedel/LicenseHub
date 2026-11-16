using LicenseHub.Application.Admins;
using LicenseHub.Application.Products;

namespace LicenseHub.Api.Endpoints;

public sealed record CreateProductRequest(string? Name, string? Slug, int? DefaultMaxActivations);

/// <summary>Null or missing fields stay unchanged. The slug cannot be changed: installed clients send it.</summary>
public sealed record UpdateProductRequest(string? Name, int? DefaultMaxActivations);

public static class AdminProductEndpoints
{
    public static RouteGroupBuilder MapAdminProducts(this RouteGroupBuilder admin)
    {
        var products = admin.MapGroup("/products").WithTags("Admin: products");

        products.MapPost("", CreateAsync)
            .WithName("AdminCreateProduct")
            .WithSummary("Creates a product.")
            .Produces<ProductView>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapGet("", ListAsync)
            .WithName("AdminListProducts")
            .WithSummary("Lists all products ordered by name.")
            .Produces<IReadOnlyList<ProductView>>();

        products.MapGet("/{id:guid}", GetAsync)
            .WithName("AdminGetProduct")
            .Produces<ProductView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("AdminUpdateProduct")
            .WithSummary("Changes the name and/or the default activation limit (applies to new licenses only).")
            .Produces<ProductView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return admin;
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        CreateProductHandler handler,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        if (request.DefaultMaxActivations is not { } maxActivations)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["defaultMaxActivations"] = ["Default max activations is required."] },
                extensions: new Dictionary<string, object?> { ["code"] = "product.invalid_max_activations" });
        }

        var result = await handler.HandleAsync(
            new CreateProductCommand(request.Name ?? string.Empty, request.Slug ?? string.Empty, maxActivations),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var view = await queries.GetProductAsync(result.Value.Id, cancellationToken);
        return TypedResults.Created($"/admin/products/{result.Value.Id}", view);
    }

    private static async Task<IReadOnlyList<ProductView>> ListAsync(IAdminQueries queries, CancellationToken cancellationToken) =>
        await queries.ListProductsAsync(cancellationToken);

    private static async Task<IResult> GetAsync(Guid id, IAdminQueries queries, CancellationToken cancellationToken) =>
        await queries.GetProductAsync(id, cancellationToken) is { } view
            ? TypedResults.Ok(view)
            : UpdateProductHandler.NotFound.ToProblem();

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        UpdateProductHandler handler,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UpdateProductCommand(id, request.Name, request.DefaultMaxActivations), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        return TypedResults.Ok(await queries.GetProductAsync(id, cancellationToken));
    }
}
