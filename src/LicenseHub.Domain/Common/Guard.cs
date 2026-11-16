namespace LicenseHub.Domain.Common;

internal static class Guard
{
    public static string RequiredText(string? value, int maxLength, string code, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException(code, $"{field} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException(code, $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string code, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException(code, $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static int ActivationLimit(int value, string code)
    {
        if (value is < 1 or > Limits.MaxActivations)
        {
            throw new DomainException(code, $"Activation limit must be between 1 and {Limits.MaxActivations}.");
        }

        return value;
    }

    public static Guid RequiredId(Guid value, string code, string field)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException(code, $"{field} is required.");
        }

        return value;
    }
}
