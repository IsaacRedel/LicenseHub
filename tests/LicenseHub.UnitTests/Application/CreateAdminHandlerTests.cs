using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Admins;
using LicenseHub.Application.Common;
using LicenseHub.Infrastructure.Auth;
using Microsoft.Extensions.Time.Testing;

namespace LicenseHub.UnitTests.Application;

public sealed class CreateAdminHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly AdminPasswordHasher _hasher = new();
    private readonly CreateAdminHandler _handler;

    public CreateAdminHandlerTests()
    {
        _handler = new CreateAdminHandler(_store, _hasher, _store, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_CreatesAdminWithHashedPassword()
    {
        var result = await Create(" Admin@Example.com ", "a long enough password");

        Assert.True(result.IsSuccess);
        var admin = Assert.Single(_store.Admins);
        Assert.Equal("admin@example.com", admin.Email);
        Assert.Equal(admin.Id, result.Value.Id);
        Assert.NotEqual("a long enough password", admin.PasswordHash);
        Assert.Equal(PasswordCheck.Success, _hasher.Verify(admin.PasswordHash, "a long enough password"));
    }

    [Fact]
    public async Task HandleAsync_RejectsAnEmailThatAlreadyExists()
    {
        await Create("admin@example.com", "a long enough password");

        var result = await Create("ADMIN@example.com", "another long password");

        Assert.Same(CreateAdminHandler.EmailTaken, result.Error);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Single(_store.Admins);
    }

    [Fact]
    public async Task HandleAsync_MapsAUniqueViolationToEmailTaken()
    {
        _store.FailNextSaveWith = new UniqueConstraintException("duplicate email");

        var result = await Create("admin@example.com", "a long enough password");

        Assert.Same(CreateAdminHandler.EmailTaken, result.Error);
    }

    [Theory]
    [InlineData("admin@example.com", "short", "admin.invalid_password")]
    [InlineData("admin@example.com", null, "admin.invalid_password")]
    [InlineData("not-an-email", "a long enough password", "admin.invalid_email")]
    public async Task HandleAsync_RejectsInvalidInput(string email, string? password, string expectedCode)
    {
        var result = await Create(email, password);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Empty(_store.Admins);
    }

    private Task<Result<CreatedAdmin>> Create(string email, string? password) =>
        _handler.HandleAsync(new CreateAdminCommand(email, password), TestContext.Current.CancellationToken);
}
