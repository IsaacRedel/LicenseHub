using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LicenseHub.Admin.Tests.Infrastructure;

/// <summary>Stands in for the LicenseHub API: answers by route and records every request.</summary>
public sealed class FakeApi : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeApi On(HttpMethod method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[$"{method} {pathAndQuery}"] = respond;
        return this;
    }

    public FakeApi OnJson(HttpMethod method, string pathAndQuery, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, pathAndQuery, _ => new HttpResponseMessage(status) { Content = JsonContent.Create(body) });

    public FakeApi OnStatus(HttpMethod method, string pathAndQuery, HttpStatusCode status) =>
        On(method, pathAndQuery, _ => new HttpResponseMessage(status));

    /// <summary>A successful login that returns a real, signed JWT for the given admin.</summary>
    public FakeApi AcceptLogin(string email = "admin@example.com", TimeSpan? lifetime = null)
    {
        var expiresAt = DateTimeOffset.UtcNow + (lifetime ?? TimeSpan.FromMinutes(30));
        IssuedToken = CreateToken(email, expiresAt);
        return OnJson(HttpMethod.Post, "/admin/auth/login", new { accessToken = IssuedToken, tokenType = "Bearer", expiresAt });
    }

    public string? IssuedToken { get; private set; }

    /// <summary>Request bodies, read when received: HttpClient disposes the content once the call returns.</summary>
    public Dictionary<HttpRequestMessage, string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add(request);
            if (body is not null)
            {
                Bodies[request] = body;
            }
        }

        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        return _routes.TryGetValue(key, out var respond)
            ? respond(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static string CreateToken(string email, DateTimeOffset expiresAt) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "licensehub",
            Audience = "licensehub-admin",
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),
                SecurityAlgorithms.HmacSha256),
        });
}
