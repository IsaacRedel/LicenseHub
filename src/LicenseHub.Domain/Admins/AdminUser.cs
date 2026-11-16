using LicenseHub.Domain.Common;

namespace LicenseHub.Domain.Admins;

/// <summary>
/// An administrator of the admin API. Login failures are counted here, so the lockout survives restarts
/// and applies across API instances.
/// </summary>
public sealed class AdminUser
{
    /// <summary>Consecutive failed logins that lock the account.</summary>
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private AdminUser()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Lowercase, unique.</summary>
    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public static AdminUser Create(string email, string passwordHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Email = NormalizeEmail(email),
        PasswordHash = RequiredHash(passwordHash),
        CreatedAt = now.ToUniversalTime(),
    };

    /// <summary>Trims and lowercases an email, then checks it is a bare address.</summary>
    public static string NormalizeEmail(string? email) =>
        Guard.Email(email, "admin.invalid_email", "Email").ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } endsAt && endsAt > now;

    /// <summary>
    /// Counts a failed login. The <see cref="MaxFailedLogins"/>th consecutive failure locks the account for
    /// <see cref="LockoutDuration"/>; the count then starts over. Callers must not count attempts made while
    /// locked out, so a lockout is not extended by further guesses.
    /// </summary>
    /// <returns>True when this failure locked the account.</returns>
    public bool RecordFailedLogin(DateTimeOffset now)
    {
        if (IsLockedOut(now))
        {
            throw new InvalidOperationException("Failed logins are not counted while the account is locked out.");
        }

        FailedLoginCount++;
        if (FailedLoginCount < MaxFailedLogins)
        {
            return false;
        }

        FailedLoginCount = 0;
        LockoutEndsAt = now.ToUniversalTime() + LockoutDuration;
        return true;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now.ToUniversalTime();
    }

    /// <summary>Replaces the hash, e.g. when the hasher reports that the stored format needs an upgrade.</summary>
    public void ChangePasswordHash(string passwordHash) => PasswordHash = RequiredHash(passwordHash);

    private static string RequiredHash(string? passwordHash) =>
        Guard.RequiredText(passwordHash, Limits.PasswordHashLength, "admin.invalid_password_hash", "Password hash");
}
