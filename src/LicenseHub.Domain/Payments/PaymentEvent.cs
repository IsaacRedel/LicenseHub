using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Payments;

/// <summary>
/// A payment gateway event that was already received. The unique (Gateway, ExternalEventId) pair
/// makes the payment webhook idempotent (stage 5).
/// </summary>
public sealed class PaymentEvent
{
    private PaymentEvent()
    {
    }

    public Guid Id { get; private set; }

    public string Gateway { get; private set; } = null!;

    public string ExternalEventId { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>The license issued for this payment, if any.</summary>
    public Guid? LicenseId { get; private set; }

    public static PaymentEvent Create(string gateway, string externalEventId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Gateway = Guard.RequiredText(gateway, Limits.GatewayLength, "payment_event.invalid_gateway", "Gateway")
            .ToLowerInvariant(),
        ExternalEventId = Guard.RequiredText(
            externalEventId,
            Limits.ExternalEventIdLength,
            "payment_event.invalid_external_id",
            "External event id"),
        ReceivedAt = now.ToUniversalTime(),
    };
}
