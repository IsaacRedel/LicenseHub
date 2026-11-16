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
        InstanceId = NormalizeInstanceId(instanceId),
        FirstValidatedAt = now.ToUniversalTime(),
        LastValidatedAt = now.ToUniversalTime(),
    };

    /// <summary>Trims and checks an instance id. Instance ids compare ordinally (case-sensitive).</summary>
    public static string NormalizeInstanceId(string? instanceId) => Guard.RequiredText(
        instanceId,
        Limits.InstanceIdLength,
        "activation.invalid_instance_id",
        "Instance id");

    /// <summary>Records a successful validation from this instance.</summary>
    public void Touch(DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        if (utcNow > LastValidatedAt)
        {
            LastValidatedAt = utcNow;
        }
    }
}
