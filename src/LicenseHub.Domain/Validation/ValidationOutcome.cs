namespace LicenseHub.Domain.Validation;

public enum ValidationOutcome
{
    Valid = 1,
    UnknownKey = 2,
    Revoked = 3,
    Expired = 4,
    ActivationLimitReached = 5,
}
