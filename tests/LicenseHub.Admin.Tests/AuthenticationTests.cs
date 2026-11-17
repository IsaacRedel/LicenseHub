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
        var expiredPage = await browser.GetStringAsync("/sessao-expirada", Token);
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
