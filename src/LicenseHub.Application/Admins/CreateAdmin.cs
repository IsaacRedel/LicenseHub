using LicenseHub.Application.Abstractions;
using LicenseHub.Application.Common;
using LicenseHub.Domain.Admins;
using LicenseHub.Domain.Common;

namespace LicenseHub.Application.Admins;

public sealed record CreateAdminCommand(string? Email, string? Password);

public sealed record CreatedAdmin(Guid Id, string Email);

/// <summary>Creates an admin account. There is no default admin: the first one is created with this use case.</summary>
public sealed class CreateAdminHandler(
    IAdminUserRepository admins,
    IAdminPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MinPasswordLength = 12;

    public static readonly Error EmailTaken = new("admin.email_taken", "An admin with this email already exists.", ErrorKind.Conflict);

    public async Task<Result<CreatedAdmin>> HandleAsync(CreateAdminCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string email;
        try
        {
            email = AdminUser.NormalizeEmail(command.Email);
        }
        catch (DomainException ex)
        {
            return new Error(ex.Code, ex.Message, Field: "email");
        }

        if (command.Password is null
            || command.Password.Length < MinPasswordLength
            || command.Password.Length > LoginHandler.MaxPasswordLength)
        {
            return new Error(
                "admin.invalid_password",
                $"Password must have {MinPasswordLength} to {LoginHandler.MaxPasswordLength} characters.",
                Field: "password");
        }

        if (await admins.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return EmailTaken;
        }

        var admin = AdminUser.Create(email, passwordHasher.Hash(command.Password), timeProvider.GetUtcNow());
        admins.Add(admin);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return EmailTaken;
        }

        return new CreatedAdmin(admin.Id, admin.Email);
    }
}
