using System.Net.Mail;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Products;

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

    private static string NormalizeEmail(string? email)
    {
        var trimmed = Guard.RequiredText(email, Limits.EmailLength, "license.invalid_customer_email", "Customer email");

        // MailAddress also accepts display names ("Name <a@b.c>"); requiring Address == input keeps bare addresses only.
        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
        {
            throw new DomainException("license.invalid_customer_email", "Customer email is not a valid address.");
        }

        return trimmed;
    }
}
