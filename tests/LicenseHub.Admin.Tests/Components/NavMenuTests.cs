using Bunit;
using LicenseHub.Admin.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LicenseHub.Admin.Tests.Components;

public sealed class NavMenuTests : BunitContext
{
    [Theory]
    [InlineData("/", "PAINEL")]
    [InlineData("/licencas", "LICENÇAS")]
    [InlineData("/licencas/0199c1d2-0000-7000-8000-000000000001", "LICENÇAS")]
    [InlineData("/produtos", "PRODUTOS")]
    [InlineData("/produtos?pagina=2", "PRODUTOS")]
    public void ActiveItem_HasAriaCurrentPage(string path, string expectedLabel)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var menu = Render<NavMenu>();

        var current = Assert.Single(menu.FindAll("a[aria-current]"));
        Assert.Equal("page", current.GetAttribute("aria-current"));
        Assert.Equal(expectedLabel, current.TextContent.Trim());
        Assert.Contains("nav__item--active", current.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveItem_FollowsNavigation()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var menu = Render<NavMenu>();
        Assert.Equal("PAINEL", menu.Find("a[aria-current]").TextContent.Trim());

        navigation.NavigateTo("/produtos");

        menu.WaitForAssertion(() => Assert.Equal("PRODUTOS", menu.Find("a[aria-current]").TextContent.Trim()));
    }

    [Fact]
    public void Markers_AreDecorative()
    {
        var menu = Render<NavMenu>();

        Assert.All(menu.FindAll(".nav__marker"), marker => Assert.Equal("true", marker.GetAttribute("aria-hidden")));
    }
}
