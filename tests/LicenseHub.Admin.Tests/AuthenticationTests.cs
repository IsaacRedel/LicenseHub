using System.Net;
using LicenseHub.Admin.Tests.Infrastructure;

namespace LicenseHub.Admin.Tests;

public sealed class AuthenticationTests : IDisposable
{
    private readonly AdminPanelFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/produtos")]
    [InlineData("/licencas")]
    [InlineData("/licencas/0199c1d2-0000-7000-8000-000000000001")]
    public async Task Pages_RedirectToLoginWithoutASession(string path)
    {
        using var browser = _factory.CreateBrowser();

        var response = await browser.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_CreatesAStrictHttpOnlySecureCookieWithoutTheJwt()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();

        var response = await LogInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        AssertRedirectsHome(response);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lh-admin=", StringComparison.Ordinal));
        var attributes = cookie.ToLowerInvariant();
        Assert.Contains("; httponly", attributes, StringComparison.Ordinal);
        Assert.Contains("; secure", attributes, StringComparison.Ordinal);
        Assert.Contains("; samesite=strict", attributes, StringComparison.Ordinal);
        Assert.Contains("; expires=", attributes, StringComparison.Ordinal);

        var jwtParts = _factory.Api.IssuedToken!.Split('.');
        Assert.All(jwtParts, part => Assert.DoesNotContain(part, cookie, StringComparison.Ordinal));
        Assert.DoesNotContain("eyJ", cookie, StringComparison.Ordinal);

        var home = await browser.GetAsync("/", Token);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task Login_SendsTheCredentialsToTheApiFromTheServer()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();

        await LogInAsync(browser);

        var login = Assert.Single(_factory.Api.Requests, r => r.RequestUri!.AbsolutePath == "/admin/auth/login");
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Null(login.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Usuário ou senha inválidos")]
    [InlineData(HttpStatusCode.TooManyRequests, "Muitas tentativas. Tente de novo mais tarde.")]
    public async Task Login_ShowsAGenericErrorWithoutEchoingThePassword(HttpStatusCode apiStatus, string message)
    {
        _factory.Api.OnStatus(HttpMethod.Post, "/admin/auth/login", apiStatus);
        using var browser = _factory.CreateBrowser();

        var response = await LogInAsync(browser, password: "segredo-que-nao-volta");
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-que-nao-volta", html, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("lh-admin=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Login_IgnoresExternalReturnUrls()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();

        var response = await LogInAsync(browser, returnUrl: "//evil.example/steal");

        AssertRedirectsHome(response);
    }

    [Fact]
    public async Task ExpiredSession_LogsOutWithAntiforgeryAndShowsTheNotice()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();
        await LogInAsync(browser);

        // What the panel does when the API answers 401: go to /sessao-expirada, which posts the logout form.
        var expiredPage = await browser.GetStringAsync(ExpiredPathFor(_factory.Api.IssuedToken!), Token);
        Assert.Contains("action=\"/logout\"", expiredPage, StringComparison.Ordinal);
        Assert.Contains("js/session-expired.js", expiredPage, StringComparison.Ordinal);

        var logout = await browser.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminPanelFactory.AntiforgeryToken(expiredPage),
            ["reason"] = "expired",
        }), Token);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/login?expirada=1", logout.Headers.Location!.OriginalString);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lh-admin=;", StringComparison.Ordinal));

        var loginPage = await browser.GetStringAsync("/login?expirada=1", Token);
        Assert.Contains("Sessão expirada", System.Net.WebUtility.HtmlDecode(loginPage), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/", Token)).StatusCode);
    }

    [Fact]
    public async Task Logout_RequiresTheAntiforgeryToken()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();
        await LogInAsync(browser);

        var logout = await browser.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>()), Token);

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/", Token)).StatusCode);
    }

    [Theory]
    [InlineData("/sessao-expirada?sessao=AAAAAAAAAAAAAAAA")]
    [InlineData("/sessao-expirada")]
    public async Task SessionExpired_OfAnotherSession_KeepsTheCurrentLogin(string path)
    {
        // A 401 in an old tab after a newer login in another tab, or a link from another site.
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();
        await LogInAsync(browser);

        var response = await browser.GetAsync(path, Token);

        AssertRedirectsHome(response);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("lh-admin=", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/", Token)).StatusCode);
    }

    [Fact]
    public async Task SessionExpired_WithoutASession_GoesStraightToTheNotice()
    {
        using var browser = _factory.CreateBrowser();

        var response = await browser.GetAsync("/sessao-expirada?sessao=AAAAAAAAAAAAAAAA", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(new Uri("https://localhost"), response.Headers.Location!);
        Assert.Equal("/login?expirada=1", location.PathAndQuery);
    }

    [Fact]
    public async Task Logout_RefusesTheSameCookieAfterwards()
    {
        _factory.Api.AcceptLogin();
        using var browser = _factory.CreateBrowser();
        var login = await LogInAsync(browser);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lh-admin=", StringComparison.Ordinal)).Split(';')[0];

        var page = await browser.GetStringAsync(ExpiredPathFor(_factory.Api.IssuedToken!), Token);
        var logout = await browser.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminPanelFactory.AntiforgeryToken(page),
        }), Token);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        // Someone who copied the cookie before the logout presents it again.
        using var replay = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", sessionCookie);
        var response = await replay.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("unreadable")]
    public async Task Login_ShowsTheApiErrorWhenTheApiTimesOutOrAnswersGarbage(string failure)
    {
        _factory.Api.On(HttpMethod.Post, "/admin/auth/login", _ => failure == "timeout"
            ? throw new TaskCanceledException("The request timed out.")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json") });
        using var browser = _factory.CreateBrowser();

        var response = await LogInAsync(browser);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LicenseHub.Admin.Components.Shared.AdminPageBase.ApiErrorMessage, html, StringComparison.Ordinal);
    }

    private static string ExpiredPathFor(string accessToken) =>
        LicenseHub.Admin.Auth.AdminAuthentication.SessionExpiredUrl(LicenseHub.Admin.Auth.TokenProvider.SessionId(accessToken));

    /// <summary>The panel redirects with an absolute URL; it must stay on the panel and land on the dashboard.</summary>
    private static void AssertRedirectsHome(HttpResponseMessage response)
    {
        var location = new Uri(new Uri("https://localhost"), response.Headers.Location!);
        Assert.Equal(("localhost", "/"), (location.Host, location.AbsolutePath));
    }

    private static async Task<HttpResponseMessage> LogInAsync(
        HttpClient browser,
        string email = "admin@example.com",
        string password = "a-long-enough-password",
        string? returnUrl = null)
    {
        var loginPath = returnUrl is null ? "/login" : $"/login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        var page = await browser.GetStringAsync(loginPath, Token);

        return await browser.PostAsync(loginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = AdminPanelFactory.AntiforgeryToken(page),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }), Token);
    }
}
