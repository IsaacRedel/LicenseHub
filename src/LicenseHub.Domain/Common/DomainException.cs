namespace LicenseHub.Domain.Common;

/// <summary>
/// Raised when an operation would break a domain invariant.
/// <see cref="Code"/> is a stable, machine-readable identifier (e.g. "product.invalid_slug").
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
