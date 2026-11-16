using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Licenses;

/// <summary>A client instance (e.g. a game server) that has used a license.</summary>
public sealed class Activation
{
    private Activation()
    {
    }

    public Guid Id { get; private set; }

    public Guid LicenseId { get; private set; }

    public string InstanceId { get; private set; } = null!;

    public DateTimeOffset FirstValidatedAt { get; private set; }

    public DateTimeOffset LastValidatedAt { get; private set; }

    public static Activation Create(Guid licenseId, string instanceId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        LicenseId = Guard.RequiredId(licenseId, "activation.invalid_license", "License"),
        InstanceId = Guard.RequiredText(
            instanceId,
            Limits.InstanceIdLength,
            "activation.invalid_instance_id",
            "Instance id"),
        FirstValidatedAt = now.ToUniversalTime(),
        LastValidatedAt = now.ToUniversalTime(),
    };
}
