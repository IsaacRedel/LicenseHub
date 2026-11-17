using System.Globalization;
using System.Net.Http.Json;

namespace LicenseHub.Admin.Api;

/// <summary>
/// Typed client for the API's /admin routes. Every call goes through <see cref="BearerTokenHandler"/>, which adds
/// the admin token and throws <see cref="SessionExpiredException"/> on 401.
/// </summary>
public sealed partial class AdminApiClient(HttpClient http)
{
    public async Task<StatsOverview> GetOverviewAsync(int utcOffsetMinutes, CancellationToken cancellationToken = default) =>
        await GetAsync<StatsOverview>(
            $"admin/stats/overview?tzOffsetMinutes={utcOffsetMinutes.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

    public async Task<IReadOnlyList<ValidationLogEntry>> GetValidationsAsync(
        int limit,
        Guid? licenseId = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"admin/validations?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (licenseId is { } id)
        {
            query += $"&licenseId={id}";
        }

        return await GetAsync<List<ValidationLogEntry>>(query, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"Empty response from {path}.");
    }
}
