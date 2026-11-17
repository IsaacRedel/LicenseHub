using Bunit;
using LicenseHub.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests.Components;

/// <summary>
/// [Authorize] must also hold for navigation inside a circuit, which never passes through the cookie middleware
/// (for example a circuit opened on the anonymous "not found" page, then a click on "Voltar ao painel").
/// </summary>
public sealed class RoutesTests : BunitContext
{
    [Theory]
    [InlineData("/", "%2F")]
    [InlineData("/licencas", "%2Flicencas")]
    public void AnonymousNavigation_GoesToLogin(string path, string returnUrl)
    {
        AddAuthorization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(path);

        var routes = Render<Routes>();

        Assert.EndsWith($"/login?ReturnUrl={returnUrl}", navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(routes.FindAll(".kpi, table, .issue-button"));
    }
}
