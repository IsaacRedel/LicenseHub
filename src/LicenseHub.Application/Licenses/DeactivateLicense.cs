using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;

namespace LicenseHub.Application.Licenses;

public sealed record DeactivateLicenseCommand(string? Key, string? InstanceId);

public sealed record DeactivatedInstance(Guid LicenseId, string InstanceId);

public sealed class DeactivateLicenseHandler(
    ILicenseRepository licenses,
    IActivationRepository activations,
    IUnitOfWork unitOfWork)
{
    /// <summary>Same error whether the key or the instance is unknown, so it does not reveal valid keys.</summary>
    public static readonly Error NotActive = new(
        "activation.not_found",
        "No active instance matches this key and instance id.",
        ErrorKind.NotFound);

    public async Task<Result<DeactivatedInstance>> HandleAsync(DeactivateLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Key) || command.Key.Length > 64)
        {
            return new Error("request.invalid_key", "Key is required and must be at most 64 characters.", Field: "key");
        }

        string instanceId;
        try
        {
            instanceId = Activation.NormalizeInstanceId(command.InstanceId);
        }
        catch (DomainException ex)
        {
            return new Error("request.invalid_instance_id", ex.Message, Field: "instanceId");
        }

        if (!LicenseKey.TryParse(command.Key, out var key))
        {
            return NotActive;
        }

        var license = await licenses.FindByKeyHashAsync(key!.ComputeHash(), cancellationToken);
        if (license is null)
        {
            return NotActive;
        }

        var activation = await activations.FindAsync(license.Id, instanceId, cancellationToken);
        if (activation is null)
        {
            return NotActive;
        }

        activations.Remove(activation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeactivatedInstance(license.Id, instanceId);
    }
}
