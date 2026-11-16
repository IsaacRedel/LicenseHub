using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Validation;

/// <summary>One validation attempt. Written by the validation flow in stage 2.</summary>
public sealed class ValidationLog
{
    private ValidationLog()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Null when the key did not match any license.</summary>
    public Guid? LicenseId { get; private set; }

    public ValidationOutcome Outcome { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ValidationLog Create(Guid? licenseId, ValidationOutcome outcome, string? ipAddress, DateTimeOffset now)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException("validation_log.invalid_outcome", "Unknown validation outcome.");
        }

        if (licenseId == Guid.Empty)
        {
            throw new DomainException("validation_log.invalid_license", "License id cannot be empty.");
        }

        return new ValidationLog
        {
            Id = Guid.CreateVersion7(now),
            LicenseId = licenseId,
            Outcome = outcome,
            IpAddress = Guard.OptionalText(ipAddress, Limits.IpAddressLength, "validation_log.invalid_ip", "IP address"),
            CreatedAt = now.ToUniversalTime(),
        };
    }
}
