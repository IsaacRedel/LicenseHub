using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Licenses;

/// <summary>A server that has used a license. Activation rules are implemented in stage 2.</summary>
public sealed class Activation
{
    private Activation()
    {
    }

    public Guid Id { get; private set; }

    public Guid LicenseId { get; private set; }

    public string ServerIdentifier { get; private set; } = null!;

    public DateTimeOffset FirstValidatedAt { get; private set; }

    public DateTimeOffset LastValidatedAt { get; private set; }

    public static Activation Create(Guid licenseId, string serverIdentifier, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        LicenseId = Guard.RequiredId(licenseId, "activation.invalid_license", "License"),
        ServerIdentifier = Guard.RequiredText(
            serverIdentifier,
            Limits.ServerIdentifierLength,
            "activation.invalid_server_identifier",
            "Server identifier"),
        FirstValidatedAt = now.ToUniversalTime(),
        LastValidatedAt = now.ToUniversalTime(),
    };
}
