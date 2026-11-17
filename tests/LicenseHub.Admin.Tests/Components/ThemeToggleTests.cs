using Bunit;
using LicenseHub.Admin.Components.Layout;

namespace LicenseHub.Admin.Tests.Components;

public sealed class ThemeToggleTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;

    public ThemeToggleTests()
    {
        _module = JSInterop.SetupModule("./js/theme.js");
    }

    [Fact]
    public void Toggle_SwitchesLabelAndAriaLabel()
    {
        _module.Setup<string>("current").SetResult("arquivo");
        _module.Setup<string>("toggle").SetResult("tatico");
        var toggle = Render<ThemeToggle>();
        var button = toggle.Find("button");
        Assert.Equal("TEMA: ARQUIVO", button.TextContent.Trim());
        Assert.Equal("Trocar para o tema Tático", button.GetAttribute("aria-label"));

        button.Click();

        toggle.WaitForAssertion(() =>
        {
            var switched = toggle.Find("button");
            Assert.Equal("TEMA: TÁTICO", switched.TextContent.Trim());
            Assert.Equal("Trocar para o tema Arquivo", switched.GetAttribute("aria-label"));
        });
    }

    [Fact]
    public void WithoutCookie_AdoptsTheThemeTheBrowserPicked()
    {
        _module.Setup<string>("current").SetResult("tatico");

        var toggle = Render<ThemeToggle>();

        toggle.WaitForAssertion(() => Assert.Equal("TEMA: TÁTICO", toggle.Find("button").TextContent.Trim()));
    }

    [Fact]
    public void SavedTheme_IsShownOnFirstRender()
    {
        _module.Setup<string>("current").SetResult("tatico");

        var toggle = Render<ThemeToggle>(p => p.Add(t => t.InitialTheme, "tatico"));

        Assert.Equal("TEMA: TÁTICO", toggle.Find("button").TextContent.Trim());
        Assert.Equal("Trocar para o tema Arquivo", toggle.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Icon_IsHiddenFromAssistiveTechnology()
    {
        _module.Setup<string>("current").SetResult("arquivo");

        var toggle = Render<ThemeToggle>();

        Assert.Equal("true", toggle.Find("svg").GetAttribute("aria-hidden"));
    }
}
