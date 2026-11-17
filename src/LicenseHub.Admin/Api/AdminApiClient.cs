using System.Diagnostics;
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

    public async Task<IReadOnlyList<ProductView>> ListProductsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<List<ProductView>>("admin/products", cancellationToken);

    public Task<ApiResult<ProductView>> CreateProductAsync(
        string name,
        string slug,
        int defaultMaxActivations,
        CancellationToken cancellationToken = default) =>
        SendAsync<ProductView>(
            HttpMethod.Post,
            "admin/products",
            new { name, slug, defaultMaxActivations },
            cancellationToken);

    /// <summary>Sends a JSON body; 4xx answers come back as <see cref="ApiProblem"/>, other failures throw.</summary>
    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var value = response.Content.Headers.ContentLength == 0
                ? default
                : await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return new ApiResult<T>(value, null);
        }

        if ((int)response.StatusCode is >= 400 and < 500)
        {
            return new ApiResult<T>(default, await ReadProblemAsync(response, cancellationToken));
        }

        response.EnsureSuccessStatusCode();
        throw new UnreachableException();
    }

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);
            return new ApiProblem((int)response.StatusCode, problem?.Code, problem?.Detail);
        }
        catch (System.Text.Json.JsonException)
        {
            return new ApiProblem((int)response.StatusCode, null, null);
        }
    }

    private sealed record ProblemBody(string? Code, string? Detail);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"Empty response from {path}.");
    }
}
