using Bunit;
using LicenseHub.Admin.Api;
using LicenseHub.Admin.Dashboard;
using LicenseHub.Admin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests.Components;

/// <summary>bUnit context for pages: the typed API client talks to <see cref="FakeApi"/>.</summary>
public abstract class PageTestContext : BunitContext
{
    protected PageTestContext()
    {
        Services.AddSingleton(new AdminApiClient(new HttpClient(Api) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new DashboardClock(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"), TimeProvider.System));
    }

    protected FakeApi Api { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Api.Dispose();
        }
    }
}
