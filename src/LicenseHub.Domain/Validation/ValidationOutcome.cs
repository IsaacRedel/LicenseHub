namespace LicenseHub.Domain.Validation;

/// <summary>Result of a validation attempt. Stored as text, so values can be added freely.</summary>
public enum ValidationOutcome
{
    Valid = 1,
    UnknownKey = 2,
    Revoked = 3,
    Expired = 4,
    ActivationLimitReached = 5,
    ProductMismatch = 6,
}
