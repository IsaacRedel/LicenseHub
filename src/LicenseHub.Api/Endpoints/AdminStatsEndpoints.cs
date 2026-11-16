using LicenseHub.Application.Admins;

namespace LicenseHub.Api.Endpoints;

public static class AdminStatsEndpoints
{
    public const int Days = 30;
    public const int TopProducts = 5;

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

        return admin;
    }

    private static Task<AdminStats> GetAsync(IAdminQueries queries, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        queries.GetStatsAsync(timeProvider.GetUtcNow(), Days, TopProducts, cancellationToken);
}
