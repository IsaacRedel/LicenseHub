using LicenseHub.Application.Abstractions;
using LicenseHub.Domain.Admins;
using Microsoft.AspNetCore.Identity;

namespace LicenseHub.Infrastructure.Auth;

/// <summary>
/// ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> on its own: PBKDF2-HMAC-SHA512 with a random
/// salt and a versioned format (100,000 iterations by default), without the Identity stores or UI.
/// </summary>
internal sealed class AdminPasswordHasher : IAdminPasswordHasher
{
    private readonly PasswordHasher<AdminUser> _hasher = new();
    private readonly Lazy<string> _dummyHash;

    public AdminPasswordHasher()
    {
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(null!, Guid.NewGuid().ToString("N")));
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        // The default hasher ignores the user argument.
        return _hasher.HashPassword(null!, password);
    }

    public PasswordCheck Verify(string? hash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (hash is null)
        {
            // Same work as a real check, so an unknown email is not faster to answer.
            _hasher.VerifyHashedPassword(null!, _dummyHash.Value, password);
            return PasswordCheck.Failed;
        }

        PasswordVerificationResult result;
        try
        {
            result = _hasher.VerifyHashedPassword(null!, hash, password);
        }
        catch (FormatException)
        {
            // A stored value that is not a hash in the Identity format can never match.
            return PasswordCheck.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }
}
