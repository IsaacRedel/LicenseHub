using System.Text.Json;
using LicenseHub.Admin.Api;
using LicenseHub.Admin.Auth;
using Microsoft.AspNetCore.Components;

namespace LicenseHub.Admin.Components.Shared;

/// <summary>Base for pages that call the API: ends the session when the API rejects the token.</summary>
public abstract class AdminPageBase : ComponentBase
{
    public const string ApiErrorMessage = "Não foi possível falar com a API. Tente de novo em instantes.";

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>The last API failure other than an expired session, shown by the page.</summary>
    protected string? ApiError { get; private set; }

    /// <summary>
    /// The API is down, timed out or answered something the panel cannot read. Shown as <see cref="ApiErrorMessage"/>
    /// instead of an unhandled exception, which would end the circuit without a message.
    /// </summary>
    public static bool IsApiFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException;

    /// <summary>
    /// Runs an API call. A 401 sends the browser to /sessao-expirada, which clears the cookie with a POST to /logout
    /// and returns to /login with the "Sessão expirada" notice.
    /// </summary>
    protected async Task<bool> CallApiAsync(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            ApiError = null;
            await call();
            return true;
        }
        catch (SessionExpiredException ex)
        {
            Navigation.NavigateTo(AdminAuthentication.SessionExpiredUrl(ex.Session), forceLoad: true);
            return false;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            ApiError = ApiErrorMessage;
            return false;
        }
    }
}
