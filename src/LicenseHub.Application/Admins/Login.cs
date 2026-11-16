using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Admins;
using LicenseHub.Domain.Common;
using Microsoft.Extensions.Logging;

namespace LicenseHub.Application.Admins;

public sealed record LoginCommand(string? Email, string? Password);

public sealed partial class LoginHandler(
    IAdminUserRepository admins,
    IAdminPasswordHasher passwordHasher,
    IAdminTokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<LoginHandler> logger)
{
    /// <summary>Longest password accepted; bounds the hashing cost of a request.</summary>
    public const int MaxPasswordLength = 256;

    /// <summary>
    /// The only failure a caller sees, whether the email is unknown, the password is wrong or the account is
    /// locked out, so the response does not reveal which admin emails exist.
    /// </summary>
    public static readonly Error InvalidCredentials = new(
        "auth.invalid_credentials",
        "Invalid email or password.",
        ErrorKind.Unauthorized);

    public async Task<Result<AdminToken>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return new Error("request.invalid_email", "Email is required.", Field: "email");
        }

        if (string.IsNullOrEmpty(command.Password) || command.Password.Length > MaxPasswordLength)
        {
            return new Error(
                "request.invalid_password",
                $"Password is required and must be at most {MaxPasswordLength} characters.",
                Field: "password");
        }

        var now = timeProvider.GetUtcNow();
        var admin = await FindAsync(command.Email, cancellationToken);
        var check = passwordHasher.Verify(admin?.PasswordHash, command.Password);

        if (admin is null)
        {
            return InvalidCredentials;
        }

        if (admin.IsLockedOut(now))
        {
            LogLockedOutAttempt(admin.Id, admin.LockoutEndsAt);
            return InvalidCredentials;
        }

        if (check == PasswordCheck.Failed)
        {
            await RecordFailureAsync(admin.Id, now, cancellationToken);
            return InvalidCredentials;
        }

        var newHash = check == PasswordCheck.SuccessRehashNeeded ? passwordHasher.Hash(command.Password) : null;
        var loggedIn = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var locked = await admins.GetForUpdateAsync(admin.Id, ct);
                if (locked is null || locked.IsLockedOut(now))
                {
                    return null;
                }

                if (newHash is not null)
                {
                    locked.ChangePasswordHash(newHash);
                }

                locked.RecordSuccessfulLogin(now);
                await unitOfWork.SaveChangesAsync(ct);
                return locked;
            },
            cancellationToken);

        return loggedIn is null ? InvalidCredentials : tokenIssuer.Issue(loggedIn, now);
    }

    private async Task<AdminUser?> FindAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            return await admins.FindByEmailAsync(AdminUser.NormalizeEmail(email), cancellationToken);
        }
        catch (DomainException)
        {
            // Not an email at all: answered like an unknown account.
            return null;
        }
    }

    private async Task RecordFailureAsync(Guid adminId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Under a row lock: parallel wrong guesses must each count, or an attacker could exceed the limit.
        var lockedOut = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var locked = await admins.GetForUpdateAsync(adminId, ct);
                if (locked is null || locked.IsLockedOut(now))
                {
                    return false;
                }

                var nowLocked = locked.RecordFailedLogin(now);
                await unitOfWork.SaveChangesAsync(ct);
                return nowLocked;
            },
            cancellationToken);

        if (lockedOut)
        {
            LogLockedOut(adminId, AdminUser.MaxFailedLogins, AdminUser.LockoutDuration.TotalMinutes);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {AdminId} locked out after {Failures} failed logins for {Minutes} minutes.")]
    private partial void LogLockedOut(Guid adminId, int failures, double minutes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login attempt for locked-out admin {AdminId}; lockout ends at {LockoutEndsAt}.")]
    private partial void LogLockedOutAttempt(Guid adminId, DateTimeOffset? lockoutEndsAt);
}
