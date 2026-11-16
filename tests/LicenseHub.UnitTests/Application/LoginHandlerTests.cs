using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Admins;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Admins;
using LicenseHub.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class LoginHandlerTests
{
    private const string Password = "correct horse battery staple";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly AdminPasswordHasher _hasher = new();
    private readonly LoginHandler _handler;
    private readonly AdminUser _admin;

    public LoginHandlerTests()
    {
        _admin = AdminUser.Create("admin@example.com", _hasher.Hash(Password), Now.AddDays(-1));
        _store.Admins.Add(_admin);

        var settings = AdminJwtSettings.FromConfiguration(new ConfigurationBuilder().Build(), allowEphemeralKey: true);
        _handler = new LoginHandler(_store, _hasher, new JwtAdminTokenIssuer(settings), _store, _time, NullLogger<LoginHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_IssuesTokenAndRecordsTheLogin()
    {
        var result = await Login(" Admin@Example.com ", Password);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.AccessToken));
        Assert.Equal(Now + AdminJwtSettings.TokenLifetime, result.Value.ExpiresAt);
        Assert.Equal(Now, _admin.LastLoginAt);
        Assert.Equal(1, _store.AdminLockCount);
    }

    [Fact]
    public async Task HandleAsync_AnswersTheSameErrorForUnknownEmailWrongPasswordAndLockout()
    {
        var unknown = await Login("nobody@example.com", Password);
        var wrong = await Login("admin@example.com", "wrong password");
        for (var i = 1; i < AdminUser.MaxFailedLogins; i++)
        {
            await Login("admin@example.com", "wrong password");
        }

        var lockedOut = await Login("admin@example.com", Password);

        Assert.All([unknown, wrong, lockedOut], r =>
        {
            Assert.Same(LoginHandler.InvalidCredentials, r.Error);
            Assert.Equal(ErrorKind.Unauthorized, r.Error!.Kind);
        });
    }

    [Fact]
    public async Task HandleAsync_LocksTheAccountAfterMaxFailures()
    {
        for (var i = 0; i < AdminUser.MaxFailedLogins; i++)
        {
            await Login("admin@example.com", "wrong password");
        }

        Assert.True(_admin.IsLockedOut(Now));
        Assert.False((await Login("admin@example.com", Password)).IsSuccess);

        // Guesses during the lockout are not counted and do not extend it.
        await Login("admin@example.com", "wrong password");
        Assert.Equal(Now + AdminUser.LockoutDuration, _admin.LockoutEndsAt);

        _time.SetUtcNow(Now + AdminUser.LockoutDuration);
        Assert.True((await Login("admin@example.com", Password)).IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_CountsFailuresUnderTheRowLock()
    {
        await Login("admin@example.com", "wrong password");

        Assert.Equal(1, _admin.FailedLoginCount);
        Assert.Equal(1, _store.AdminLockCount);
    }

    [Fact]
    public async Task HandleAsync_ResetsTheFailureCountOnSuccess()
    {
        await Login("admin@example.com", "wrong password");
        await Login("admin@example.com", "wrong password");

        await Login("admin@example.com", Password);

        Assert.Equal(0, _admin.FailedLoginCount);
    }

    [Theory]
    [InlineData(null, Password, "request.invalid_email")]
    [InlineData("  ", Password, "request.invalid_email")]
    [InlineData("admin@example.com", null, "request.invalid_password")]
    [InlineData("admin@example.com", "", "request.invalid_password")]
    public async Task HandleAsync_RejectsMissingFields(string? email, string? password, string expectedCode)
    {
        var result = await Login(email, password);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal(0, _admin.FailedLoginCount);
    }

    [Fact]
    public async Task HandleAsync_RejectsOverlongPasswordsBeforeHashing()
    {
        var result = await Login("admin@example.com", new string('x', LoginHandler.MaxPasswordLength + 1));

        Assert.Equal("request.invalid_password", result.Error!.Code);
    }

    [Fact]
    public async Task HandleAsync_TreatsANonEmailLikeAnUnknownAccount()
    {
        var result = await Login("not-an-email", Password);

        Assert.Same(LoginHandler.InvalidCredentials, result.Error);
    }

    private Task<Result<AdminToken>> Login(string? email, string? password) =>
        _handler.HandleAsync(new LoginCommand(email, password), TestContext.Current.CancellationToken);
}
