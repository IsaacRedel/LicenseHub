using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Admins;
using LicenseHub.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LicenseHub.UnitTests.Infrastructure;

public sealed class AdminAuthTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly AdminPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesSaltedHashesThatVerify()
    {
        var first = _hasher.Hash("correct horse battery staple");
        var second = _hasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("correct horse", first, StringComparison.Ordinal);
        Assert.Equal(PasswordCheck.Success, _hasher.Verify(first, "correct horse battery staple"));
        Assert.Equal(PasswordCheck.Success, _hasher.Verify(second, "correct horse battery staple"));
    }

    [Theory]
    [InlineData("wrong password")]
    [InlineData("Correct horse battery staple")]
    [InlineData("")]
    public void Verify_FailsForAnyOtherPassword(string attempt)
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(hash, attempt));
    }

    [Fact]
    public void Verify_FailsWithoutAHash()
    {
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(null, "anything"));
    }

    [Fact]
    public void Verify_FailsForAGarbageHash()
    {
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify("not-a-hash", "anything"));
    }

    [Fact]
    public async Task IssuedToken_ValidatesWithTheSettingsAndCarriesTheAdmin()
    {
        var settings = Settings(AdminJwtSettings.GenerateKey());
        var admin = AdminUser.Create("admin@example.com", "hash", Now);

        var token = new JwtAdminTokenIssuer(settings).Issue(admin, Now);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, settings.CreateValidationParameters());

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(admin.Id.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("admin@example.com", result.Claims[JwtRegisteredClaimNames.Email]);
        Assert.True(result.Claims.ContainsKey(JwtRegisteredClaimNames.Jti));
        Assert.Equal(Now.AddMinutes(30).ToUnixTimeSeconds(), token.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task IssuedToken_FailsWithAnotherKey()
    {
        var admin = AdminUser.Create("admin@example.com", "hash", Now);
        var token = new JwtAdminTokenIssuer(Settings(AdminJwtSettings.GenerateKey())).Issue(admin, Now);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.AccessToken,
            Settings(AdminJwtSettings.GenerateKey()).CreateValidationParameters());

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenSignatureKeyNotFoundException>(result.Exception);
    }

    [Fact]
    public async Task IssuedToken_ExpiresAfterThirtyMinutesPlusSkew()
    {
        var settings = Settings(AdminJwtSettings.GenerateKey());
        var admin = AdminUser.Create("admin@example.com", "hash", Now);
        var issuedLongAgo = Now - AdminJwtSettings.TokenLifetime - AdminJwtSettings.ClockSkew - TimeSpan.FromSeconds(5);

        var token = new JwtAdminTokenIssuer(settings).Issue(admin, issuedLongAgo);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, settings.CreateValidationParameters());

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public void Settings_RequireAKeyOutsideDevelopment()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AdminJwtSettings.FromConfiguration(new ConfigurationBuilder().Build(), allowEphemeralKey: false));

        Assert.Contains("AdminAuth:JwtKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_RejectKeysShorterThan256Bits()
    {
        var shortKey = Convert.ToBase64String(new byte[31]);

        var error = Assert.Throws<InvalidOperationException>(() => Settings(shortKey));

        Assert.Contains("256 bits", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_UseAnEphemeralKeyOnlyWhenAllowed()
    {
        var settings = AdminJwtSettings.FromConfiguration(new ConfigurationBuilder().Build(), allowEphemeralKey: true);

        Assert.True(settings.IsEphemeral);
        Assert.False(Settings(AdminJwtSettings.GenerateKey()).IsEphemeral);
    }

    private static AdminJwtSettings Settings(string key) => AdminJwtSettings.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminAuth:JwtKey"] = key }).Build(),
        allowEphemeralKey: false);
}
