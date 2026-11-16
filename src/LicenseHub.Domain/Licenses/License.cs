using LicenseHub.Domain.Common;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.Domain.Licenses;

public sealed class License
{
    private License()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProductId { get; private set; }

    public string CustomerEmail { get; private set; } = null!;

    /// <summary>SHA-256 of the license key. The plain key is never stored.</summary>
    public byte[] KeyHash { get; private set; } = null!;

    public LicenseStatus Status { get; private set; }

    /// <summary>Null means the license never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public int MaxActivations { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    /// <summary>The admin who revoked the license; null if revoked by the system or the admin was removed.</summary>
    public Guid? RevokedByAdminId { get; private set; }

    /// <summary>
    /// Issues an active license for <paramref name="product"/>.
    /// When <paramref name="maxActivations"/> is null the product default is used.
    /// </summary>
    public static License Issue(
        Product product,
        string customerEmail,
        byte[] keyHash,
        int? maxActivations,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(keyHash);

        if (keyHash.Length != LicenseKey.HashLength)
        {
            throw new DomainException("license.invalid_key_hash", $"Key hash must be {LicenseKey.HashLength} bytes.");
        }

        if (expiresAt is { } expiry && expiry <= now)
        {
            throw new DomainException("license.invalid_expiration", "Expiration must be in the future.");
        }

        return new License
        {
            Id = Guid.CreateVersion7(now),
            ProductId = product.Id,
            CustomerEmail = NormalizeEmail(customerEmail),
            KeyHash = (byte[])keyHash.Clone(),
            Status = LicenseStatus.Active,
            ExpiresAt = expiresAt?.ToUniversalTime(),
            MaxActivations = Guard.ActivationLimit(
                maxActivations ?? product.DefaultMaxActivations,
                "license.invalid_max_activations"),
            CreatedAt = now.ToUniversalTime(),
        };
    }

    /// <summary>Whether the license can be used right now, ignoring activations.</summary>
    public ValidationOutcome CheckUsable(DateTimeOffset now)
    {
        if (Status == LicenseStatus.Revoked)
        {
            return ValidationOutcome.Revoked;
        }

        if (Status == LicenseStatus.Expired || (ExpiresAt is { } expiry && expiry <= now))
        {
            return ValidationOutcome.Expired;
        }

        return ValidationOutcome.Valid;
    }

    /// <summary>
    /// Validates the license for <paramref name="instanceId"/>. An instance that is already active is
    /// refreshed; a new instance is activated only while the license is below <see cref="MaxActivations"/>.
    /// </summary>
    /// <param name="activations">All current activations of this license.</param>
    public ActivationResult Activate(IReadOnlyCollection<Activation> activations, string instanceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activations);
        if (activations.Any(a => a.LicenseId != Id))
        {
            throw new ArgumentException("Activations must belong to this license.", nameof(activations));
        }

        var normalizedInstanceId = Activation.NormalizeInstanceId(instanceId);

        var usable = CheckUsable(now);
        if (usable != ValidationOutcome.Valid)
        {
            return ActivationResult.Rejected(usable);
        }

        var existing = activations.FirstOrDefault(a => string.Equals(a.InstanceId, normalizedInstanceId, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Touch(now);
            return ActivationResult.Accepted(existing, isNew: false);
        }

        if (activations.Count >= MaxActivations)
        {
            return ActivationResult.Rejected(ValidationOutcome.ActivationLimitReached);
        }

        return ActivationResult.Accepted(Activation.Create(Id, normalizedInstanceId, now), isNew: true);
    }

    /// <summary>
    /// Status as of <paramref name="now"/>: an active license past <see cref="ExpiresAt"/> counts as expired even
    /// before a job stores <see cref="LicenseStatus.Expired"/>.
    /// </summary>
    public LicenseStatus EffectiveStatus(DateTimeOffset now) => CheckUsable(now) switch
    {
        ValidationOutcome.Revoked => LicenseStatus.Revoked,
        ValidationOutcome.Expired => LicenseStatus.Expired,
        _ => LicenseStatus.Active,
    };

    /// <summary>
    /// Revokes the license. Idempotent: revoking an already revoked license changes nothing and keeps the
    /// original date, reason and admin.
    /// </summary>
    /// <returns>True when this call revoked the license; false when it was already revoked.</returns>
    public bool Revoke(DateTimeOffset now, string reason, Guid? revokedByAdminId = null)
    {
        var normalizedReason = Guard.RequiredText(reason, Limits.RevocationReasonLength, "license.invalid_revocation_reason", "Reason");
        if (revokedByAdminId == Guid.Empty)
        {
            throw new DomainException("license.invalid_revoked_by", "Revoking admin id cannot be empty.");
        }

        if (Status == LicenseStatus.Revoked)
        {
            return false;
        }

        Status = LicenseStatus.Revoked;
        RevokedAt = now.ToUniversalTime();
        RevocationReason = normalizedReason;
        RevokedByAdminId = revokedByAdminId;
        return true;
    }

    private static string NormalizeEmail(string? email) =>
        Guard.Email(email, "license.invalid_customer_email", "Customer email");
}
