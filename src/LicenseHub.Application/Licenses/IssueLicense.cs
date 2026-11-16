using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;

namespace LicenseHub.Application.Licenses;

/// <param name="MaxActivations">Null uses the product default.</param>
/// <param name="ExpiresAt">Null issues a license that never expires.</param>
public sealed record IssueLicenseCommand(
    Guid ProductId,
    string CustomerEmail,
    int? MaxActivations = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// The only place where the plain <see cref="Key"/> exists: it is not stored and cannot be recovered later.
/// </summary>
public sealed record IssuedLicense(
    Guid LicenseId,
    Guid ProductId,
    string Key,
    string CustomerEmail,
    int MaxActivations,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt)
{
    // Records print every property by default; keep the key out of logs.
    public override string ToString() =>
        $"IssuedLicense {{ LicenseId = {LicenseId}, ProductId = {ProductId}, Key = LH-****-****-****-**** }}";
}

public sealed class IssueLicenseHandler(
    IProductRepository products,
    ILicenseRepository licenses,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly Error ProductNotFound = new("license.product_not_found", "The product does not exist.", ErrorKind.NotFound);

    public async Task<Result<IssuedLicense>> HandleAsync(IssueLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductNotFound;
        }

        var key = LicenseKey.Generate();

        License license;
        try
        {
            license = License.Issue(
                product,
                command.CustomerEmail,
                key.ComputeHash(),
                command.MaxActivations,
                command.ExpiresAt,
                timeProvider.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return new Error(ex.Code, ex.Message);
        }

        licenses.Add(license);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedLicense(
            license.Id,
            license.ProductId,
            key.Value,
            license.CustomerEmail,
            license.MaxActivations,
            license.ExpiresAt,
            license.CreatedAt);
    }
}
