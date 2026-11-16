using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;

namespace LicenseHub.Application.Products;

/// <summary>Null fields stay unchanged. The slug cannot be changed.</summary>
public sealed record UpdateProductCommand(Guid ProductId, string? Name, int? DefaultMaxActivations);

public sealed class UpdateProductHandler(IProductRepository products, IUnitOfWork unitOfWork)
{
    public static readonly Error NotFound = new("product.not_found", "The product does not exist.", ErrorKind.NotFound);

    public async Task<Result<Guid>> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return NotFound;
        }

        try
        {
            product.Update(command.Name, command.DefaultMaxActivations);
        }
        catch (DomainException ex)
        {
            var field = ex.Code == "product.invalid_name" ? "name" : "defaultMaxActivations";
            return new Error(ex.Code, ex.Message, Field: field);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.Id;
    }
}
