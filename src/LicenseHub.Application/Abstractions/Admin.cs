using LicenseHub.Domain.Admins;

namespace LicenseHub.Application.Abstractions;

public interface IAdminUserRepository
{
    /// <summary>Expects an already normalized email (see <see cref="AdminUser.NormalizeEmail"/>).</summary>
    Task<AdminUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the admin and locks the row until the transaction ends, so concurrent failed logins cannot lose
    /// updates to the failure counter. Must run inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<AdminUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(AdminUser admin);
}

public enum PasswordCheck
{
    Failed = 0,
    Success = 1,

    /// <summary>Correct password stored in an older hash format; the caller should store a new hash.</summary>
    SuccessRehashNeeded = 2,
}

public interface IAdminPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks <paramref name="password"/> against <paramref name="hash"/>. With a null hash it checks against a
    /// fixed dummy hash and returns <see cref="PasswordCheck.Failed"/>, so an unknown email costs the same time
    /// as a wrong password.
    /// </summary>
    PasswordCheck Verify(string? hash, string password);
}

public sealed record AdminToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface IAdminTokenIssuer
{
    AdminToken Issue(AdminUser admin, DateTimeOffset now);
}
