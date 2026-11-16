using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Products;

namespace LicenseHub.Application.Products;

public sealed record CreateProductCommand(string Name, string Slug, int DefaultMaxActivations);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    int DefaultMaxActivations,
    DateTimeOffset CreatedAt)
{
    public static ProductResponse From(Product product) => new(
        product.Id,
        product.Name,
        product.Slug,
        product.DefaultMaxActivations,
        product.CreatedAt);
}

public sealed class CreateProductHandler(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly Error SlugTaken = new("product.slug_taken", "A product with this slug already exists.", ErrorKind.Conflict);

    public async Task<Result<ProductResponse>> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Product product;
        try
        {
            product = Product.Create(command.Name, command.Slug, command.DefaultMaxActivations, timeProvider.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return new Error(ex.Code, ex.Message);
        }

        if (await products.SlugExistsAsync(product.Slug, cancellationToken))
        {
            return SlugTaken;
        }

        products.Add(product);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // Another request created the same slug between the check and the insert.
            return SlugTaken;
        }

        return ProductResponse.From(product);
    }
}
