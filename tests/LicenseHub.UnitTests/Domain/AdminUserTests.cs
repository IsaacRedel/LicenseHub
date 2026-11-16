using LicenseHub.Domain.Admins;
using LicenseHub.Domain.Common;

namespace LicenseHub.UnitTests.Domain;

public sealed class AdminUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NormalizesEmail()
    {
        var admin = AdminUser.Create("  Admin@Example.COM ", "hash", Now);

        Assert.Equal("admin@example.com", admin.Email);
        Assert.Equal(0, admin.FailedLoginCount);
        Assert.Null(admin.LastLoginAt);
        Assert.False(admin.IsLockedOut(Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Admin <admin@example.com>")]
    public void Create_RejectsInvalidEmail(string email)
    {
        var error = Assert.Throws<DomainException>(() => AdminUser.Create(email, "hash", Now));

        Assert.Equal("admin.invalid_email", error.Code);
    }

    [Fact]
    public void RecordFailedLogin_LocksAfterMaxConsecutiveFailures()
    {
        var admin = AdminUser.Create("admin@example.com", "hash", Now);

        for (var i = 1; i < AdminUser.MaxFailedLogins; i++)
        {
            Assert.False(admin.RecordFailedLogin(Now));
            Assert.False(admin.IsLockedOut(Now));
        }

        Assert.True(admin.RecordFailedLogin(Now));
        Assert.True(admin.IsLockedOut(Now));
        Assert.Equal(Now + AdminUser.LockoutDuration, admin.LockoutEndsAt);
    }

    [Fact]
    public void Lockout_EndsAfterTheDuration()
    {
        var admin = LockedOut();

        Assert.True(admin.IsLockedOut(Now + AdminUser.LockoutDuration - TimeSpan.FromSeconds(1)));
        Assert.False(admin.IsLockedOut(Now + AdminUser.LockoutDuration));
    }

    [Fact]
    public void RecordFailedLogin_RefusesToCountWhileLockedOut()
    {
        var admin = LockedOut();

        Assert.Throws<InvalidOperationException>(() => admin.RecordFailedLogin(Now.AddMinutes(1)));
        Assert.Equal(Now + AdminUser.LockoutDuration, admin.LockoutEndsAt);
    }

    [Fact]
    public void RecordFailedLogin_StartsCountingAgainAfterLockout()
    {
        var admin = LockedOut();
        var afterLockout = Now + AdminUser.LockoutDuration;

        Assert.False(admin.RecordFailedLogin(afterLockout));
        Assert.Equal(1, admin.FailedLoginCount);
        Assert.False(admin.IsLockedOut(afterLockout));
    }

    [Fact]
    public void RecordSuccessfulLogin_ResetsFailuresAndStoresTheDate()
    {
        var admin = AdminUser.Create("admin@example.com", "hash", Now);
        admin.RecordFailedLogin(Now);
        admin.RecordFailedLogin(Now);

        admin.RecordSuccessfulLogin(Now.AddMinutes(1));

        Assert.Equal(0, admin.FailedLoginCount);
        Assert.Null(admin.LockoutEndsAt);
        Assert.Equal(Now.AddMinutes(1), admin.LastLoginAt);
    }

    private static AdminUser LockedOut()
    {
        var admin = AdminUser.Create("admin@example.com", "hash", Now);
        for (var i = 0; i < AdminUser.MaxFailedLogins; i++)
        {
            admin.RecordFailedLogin(Now);
        }

        return admin;
    }
}
