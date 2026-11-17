using System.Globalization;
using LicenseHub.Application.Admins;
using LicenseHub.Application.Common;

namespace LicenseHub.Api.Endpoints;

public static class AdminStatsEndpoints
{
    public const int Days = 30;
    public const int TopProducts = 5;

    /// <summary>UTC-14:00 to UTC+14:00, the range of real time zones.</summary>
    public const int MaxUtcOffsetMinutes = 14 * 60;

    public const int DefaultValidationsLimit = 10;
    public const int MaxValidationsLimit = 100;

    public static RouteGroupBuilder MapAdminStats(this RouteGroupBuilder admin)
    {
        admin.MapGet("/stats", GetAsync)
            .WithTags("Admin: stats")
            .WithName("AdminStats")
            .WithSummary("License counts by status, validations per day and top products.")
            .WithDescription(
                $"validationsPerDay covers the last {Days} UTC days (today included), with zeros for days without " +
                $"validations. topProducts lists the {TopProducts} products with most validations in that period; " +
                "unknown_key attempts have no license and therefore no product.")
            .Produces<AdminStats>();

        admin.MapGet("/stats/overview", GetOverviewAsync)
            .WithTags("Admin: stats")
            .WithName("AdminStatsOverview")
            .WithSummary("Dashboard data: KPIs, today's validations by reason, 30 days accepted × refused and product usage.")
            .WithDescription(
                "tzOffsetMinutes (default 0, from -840 to 840) sets the calendar day: today and the last 30 days are " +
                "local days at that offset from UTC. Product usage counts only active licenses.")
            .Produces<StatsOverview>()
            .ProducesValidationProblem();

        admin.MapGet("/validations", ListValidationsAsync)
            .WithTags("Admin: validations")
            .WithName("AdminListValidations")
            .WithSummary("Most recent validation attempts, newest first.")
            .WithDescription(
                $"limit defaults to {DefaultValidationsLimit} and is at most {MaxValidationsLimit}; licenseId limits " +
                "the list to one license. Never includes keys or key hashes.")
            .Produces<IReadOnlyList<ValidationLogView>>()
            .ProducesValidationProblem();

        return admin;
    }

    private static Task<AdminStats> GetAsync(IAdminQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        queries.GetStatsAsync(timeProvider.GetUtcNow(), Days, TopProducts, cancellationToken);

    private static async Task<IResult> GetOverviewAsync(
        string? tzOffsetMinutes,
        IAdminQueries queries,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(tzOffsetMinutes)
            && (!int.TryParse(tzOffsetMinutes, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offset)
                || Math.Abs(offset) > MaxUtcOffsetMinutes))
        {
            return new Error(
                "request.invalid_tz_offset",
                $"tzOffsetMinutes must be a whole number between -{MaxUtcOffsetMinutes} and {MaxUtcOffsetMinutes}.",
                Field: "tzOffsetMinutes").ToProblem();
        }

        return TypedResults.Ok(await queries.GetOverviewAsync(timeProvider.GetUtcNow(), offset, cancellationToken));
    }

    private static async Task<IResult> ListValidationsAsync(
        string? limit,
        string? licenseId,
        IAdminQueries queries,
        CancellationToken cancellationToken)
    {
        var take = DefaultValidationsLimit;
        if (!string.IsNullOrWhiteSpace(limit)
            && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out take) || take is < 1 or > MaxValidationsLimit))
        {
            return new Error(
                "request.invalid_limit",
                $"limit must be between 1 and {MaxValidationsLimit}.",
                Field: "limit").ToProblem();
        }

        Guid? license = null;
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            if (!Guid.TryParse(licenseId, out var parsed))
            {
                return new Error("request.invalid_license_id", "licenseId must be a GUID.", Field: "licenseId").ToProblem();
            }

            license = parsed;
        }

        return TypedResults.Ok(await queries.ListValidationsAsync(take, license, cancellationToken));
    }
}
