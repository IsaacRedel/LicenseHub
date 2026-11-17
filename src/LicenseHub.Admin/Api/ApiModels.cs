namespace LicenseHub.Admin.Api;

// Shapes of the API's /admin responses (camelCase JSON). Enums arrive as camelCase strings.

public sealed record OverviewKpis(int ActiveLicenses, int RevokedLicenses, int ExpiredLicenses, int ValidationsToday);

public sealed record TodayValidations(
    DateOnly Date,
    int Total,
    int Accepted,
    int UnknownKey,
    int ProductMismatch,
    int Revoked,
    int Expired,
    int ActivationLimitReached)
{
    public int Refused => Total - Accepted;
}

public sealed record DayValidations(DateOnly Date, int Accepted, int Refused)
{
    public int Total => Accepted + Refused;
}

public sealed record ProductUsage(
    Guid ProductId,
    string Name,
    string Slug,
    int ActiveLicenses,
    int ActivationsUsed,
    int ActivationCapacity);

public sealed record StatsOverview(
    OverviewKpis Kpis,
    TodayValidations Today,
    IReadOnlyList<DayValidations> Last30Days,
    IReadOnlyList<ProductUsage> Products);

public sealed record ProductView(
    Guid Id,
    string Name,
    string Slug,
    int DefaultMaxActivations,
    DateTimeOffset CreatedAt,
    int LicenseCount);

/// <summary>An RFC 9457 problem from the API, reduced to what the panel shows.</summary>
/// <param name="Code">The API's stable error code, e.g. "product.slug_taken".</param>
public sealed record ApiProblem(int Status, string? Code, string? Detail);

/// <summary>Either the created resource or the problem the API answered.</summary>
public sealed record ApiResult<T>(T? Value, ApiProblem? Problem)
{
    public bool IsSuccess => Problem is null;
}

/// <param name="Outcome">valid, unknownKey, productMismatch, revoked, expired or activationLimitReached.</param>
public sealed record ValidationLogEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Outcome,
    Guid? LicenseId,
    string? ProductSlug,
    string? IpAddress);
