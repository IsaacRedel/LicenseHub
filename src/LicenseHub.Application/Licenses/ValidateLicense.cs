using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Common;
using LicenseHub.Domain.Licenses;
using LicenseHub.Domain.Products;
using LicenseHub.Domain.Validation;

namespace LicenseHub.Application.Licenses;

/// <param name="Timestamp">Client clock, in Unix seconds.</param>
/// <param name="IpAddress">Caller address, recorded in the validation log.</param>
public sealed record ValidateLicenseCommand(
    string? Key,
    string? ProductSlug,
    string? InstanceId,
    string? Nonce,
    long? Timestamp,
    string? IpAddress = null);

/// <summary>
/// The validation verdict to be signed and sent back. <see cref="LicenseId"/> and <see cref="ExpiresAt"/> are
/// null when the key is unknown or belongs to another product.
/// </summary>
/// <param name="ProductSlug">The normalized slug the verdict is for, so a response for one product cannot
/// be passed off as a response for another.</param>
/// <param name="KeyFingerprint">Binds the verdict to the key the client sent (see
/// <see cref="ValidateLicenseHandler.ComputeKeyFingerprint"/>), so a proxy cannot swap in its own key.</param>
public sealed record LicenseValidation(
    ValidationOutcome Outcome,
    Guid? LicenseId,
    DateTimeOffset? ExpiresAt,
    string ProductSlug,
    string KeyFingerprint,
    string Nonce,
    string InstanceId,
    DateTimeOffset IssuedAt)
{
    public bool IsValid => Outcome == ValidationOutcome.Valid;
}

public sealed partial class ValidateLicenseHandler(
    IProductRepository products,
    ILicenseRepository licenses,
    IActivationRepository activations,
    IValidationLogRepository validationLogs,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>How far the client timestamp may be from the server clock, in either direction.</summary>
    public static readonly TimeSpan TimestampWindow = TimeSpan.FromMinutes(5);

    public const int NonceMinLength = 16;
    public const int NonceMaxLength = 128;

    public async Task<Result<LicenseValidation>> HandleAsync(ValidateLicenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = timeProvider.GetUtcNow();

        if (CheckInput(command, now) is { } inputError)
        {
            return inputError;
        }

        var nonce = command.Nonce!;
        var instanceId = Activation.NormalizeInstanceId(command.InstanceId);
        var slug = Product.NormalizeSlug(command.ProductSlug);

        var (outcome, license) = await EvaluateAsync(command.Key!, slug, instanceId, now, cancellationToken);

        validationLogs.Add(ValidationLog.Create(license?.Id, outcome, command.IpAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Do not reveal anything about a license that belongs to another product.
        var visible = outcome is ValidationOutcome.UnknownKey or ValidationOutcome.ProductMismatch ? null : license;

        return new LicenseValidation(
            outcome,
            visible?.Id,
            visible?.ExpiresAt,
            slug,
            ComputeKeyFingerprint(command.Key!),
            nonce,
            instanceId,
            now);
    }

    /// <summary>
    /// First 16 base64url characters of the SHA-256 of the key exactly as the client sent it (UTF-8, not
    /// normalized), so the client can compare it with the hash of the string it sent.
    /// </summary>
    public static string ComputeKeyFingerprint(string key) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

    private async Task<(ValidationOutcome Outcome, License? License)> EvaluateAsync(
        string key,
        string slug,
        string instanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!LicenseKey.TryParse(key, out var licenseKey))
        {
            return (ValidationOutcome.UnknownKey, null);
        }

        var license = await licenses.FindByKeyHashAsync(licenseKey!.ComputeHash(), cancellationToken);
        if (license is null)
        {
            return (ValidationOutcome.UnknownKey, null);
        }

        var product = await products.GetBySlugAsync(slug, cancellationToken);
        if (product is null || product.Id != license.ProductId)
        {
            return (ValidationOutcome.ProductMismatch, license);
        }

        var usable = license.CheckUsable(now);
        if (usable != ValidationOutcome.Valid)
        {
            return (usable, license);
        }

        // Lock the license row so concurrent new instances cannot exceed MaxActivations.
        return await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var locked = await licenses.GetForUpdateAsync(license.Id, ct);
                if (locked is null)
                {
                    return (ValidationOutcome.UnknownKey, (License?)null);
                }

                var current = await activations.ListByLicenseAsync(locked.Id, ct);
                var result = locked.Activate(current, instanceId, now);
                if (result.IsNewActivation)
                {
                    activations.Add(result.Activation!);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return (result.Outcome, (License?)locked);
            },
            cancellationToken);
    }

    private static Error? CheckInput(ValidateLicenseCommand command, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(command.Key) || command.Key.Length > 64)
        {
            return new Error("request.invalid_key", "Key is required and must be at most 64 characters.", Field: "key");
        }

        try
        {
            Product.NormalizeSlug(command.ProductSlug);
        }
        catch (DomainException ex)
        {
            return new Error("request.invalid_product_slug", ex.Message, Field: "productSlug");
        }

        try
        {
            Activation.NormalizeInstanceId(command.InstanceId);
        }
        catch (DomainException ex)
        {
            return new Error("request.invalid_instance_id", ex.Message, Field: "instanceId");
        }

        if (command.Nonce is null || !NoncePattern().IsMatch(command.Nonce))
        {
            return new Error(
                "request.invalid_nonce",
                $"Nonce must have {NonceMinLength} to {NonceMaxLength} base64url characters (A-Z, a-z, 0-9, '-', '_').",
                Field: "nonce");
        }

        if (command.Timestamp is not { } timestamp)
        {
            return new Error("request.invalid_timestamp", "Timestamp (Unix seconds) is required.", Field: "timestamp");
        }

        var skewSeconds = Math.Abs((double)timestamp - now.ToUnixTimeSeconds());
        if (skewSeconds > TimestampWindow.TotalSeconds)
        {
            return new Error(
                "request.timestamp_out_of_window",
                $"Timestamp must be within {TimestampWindow.TotalMinutes:0} minutes of the server clock.",
                Field: "timestamp");
        }

        return null;
    }

    // \z, not $: in .NET, $ also matches before a trailing "\n".
    [GeneratedRegex(@"^[A-Za-z0-9_-]{16,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NoncePattern();
}
