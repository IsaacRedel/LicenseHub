using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;

namespace LicenseHub.Application.Admins;

public static class LicenseErrors
{
    public static readonly Error NotFound = new("license.not_found", "The license does not exist.", ErrorKind.NotFound);

    public static readonly Error ActivationNotFound = new(
        "activation.not_found",
        "The license has no active instance with this id.",
        ErrorKind.NotFound);
}

/// <param name="Status">"active", "revoked" or "expired" (effective status), or null for any.</param>
public sealed record ListLicensesQuery(string? Status, string? ProductSlug, int? Page, int? PageSize);

public sealed class ListLicensesHandler(IAdminQueries queries, TimeProvider timeProvider)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Keeps (page - 1) * pageSize inside int for any page size.</summary>
    public const int MaxPage = int.MaxValue / MaxPageSize;

    public async Task<Result<PagedResult<LicenseListItem>>> HandleAsync(ListLicensesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        LicenseStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // An explicit list: Enum.TryParse would also accept numbers and comma-separated names.
            status = query.Status.Trim().ToLowerInvariant() switch
            {
                "active" => LicenseStatus.Active,
                "revoked" => LicenseStatus.Revoked,
                "expired" => LicenseStatus.Expired,
                _ => null,
            };

            if (status is null)
            {
                return new Error("request.invalid_status", "Status must be active, revoked or expired.", Field: "status");
            }
        }

        var page = query.Page ?? 1;
        if (page is < 1 or > MaxPage)
        {
            return new Error("request.invalid_page", $"Page must be between 1 and {MaxPage}.", Field: "page");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            return new Error("request.invalid_page_size", $"Page size must be between 1 and {MaxPageSize}.", Field: "pageSize");
        }

        var slug = string.IsNullOrWhiteSpace(query.ProductSlug) ? null : query.ProductSlug.Trim().ToLowerInvariant();

        return await queries.ListLicensesAsync(
            new LicenseFilter(status, slug, page, pageSize, timeProvider.GetUtcNow()),
            cancellationToken);
    }
}

public sealed record RevokeLicenseCommand(Guid LicenseId, string? Reason, Guid AdminId);

/// <summary>Idempotent: revoking an already revoked license keeps the original date, reason and admin.</summary>
public sealed class RevokeLicenseHandler(ILicenseRepository licenses, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result<Guid>> HandleAsync(RevokeLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = timeProvider.GetUtcNow();

        try
        {
            // Under the license row lock, so two concurrent revocations cannot overwrite each other's reason
            // and a validation in progress finishes before the status changes.
            var found = await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var license = await licenses.GetForUpdateAsync(command.LicenseId, ct);
                    if (license is null)
                    {
                        return false;
                    }

                    if (license.Revoke(now, command.Reason ?? string.Empty, command.AdminId))
                    {
                        await unitOfWork.SaveChangesAsync(ct);
                    }

                    return true;
                },
                cancellationToken);

            return found ? command.LicenseId : LicenseErrors.NotFound;
        }
        catch (DomainException ex)
        {
            return new Error(ex.Code, ex.Message, Field: "reason");
        }
    }
}

public sealed record ReleaseActivationCommand(Guid LicenseId, string? InstanceId);

/// <summary>Frees one activation slot of a license, under the same row lock as validation.</summary>
public sealed class ReleaseActivationHandler(ILicenseRepository licenses, IActivationRepository activations, IUnitOfWork unitOfWork)
{
    public async Task<Result<Guid>> HandleAsync(ReleaseActivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string instanceId;
        try
        {
            instanceId = Activation.NormalizeInstanceId(command.InstanceId);
        }
        catch (DomainException)
        {
            return LicenseErrors.ActivationNotFound;
        }

        var outcome = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var license = await licenses.GetForUpdateAsync(command.LicenseId, ct);
                if (license is null)
                {
                    return LicenseErrors.NotFound;
                }

                var activation = await activations.FindAsync(license.Id, instanceId, ct);
                if (activation is null)
                {
                    return LicenseErrors.ActivationNotFound;
                }

                activations.Remove(activation);
                await unitOfWork.SaveChangesAsync(ct);
                return null;
            },
            cancellationToken);

        return outcome is null ? command.LicenseId : outcome;
    }
}
