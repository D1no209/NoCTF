using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;
using Npgsql;

namespace NoCTF.API.Endpoints.Auth;

public class RegisterRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool RequiresEmailVerification { get; set; }
    public bool VerificationEmailSent { get; set; }
}

public class RegisterEndpoint(
    ApplicationDbContext dbContext,
    IEmailVerificationService emailVerification) : Endpoint<RegisterRequest, RegisterResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/auth/register");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("auth-register"));
    }

    public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
    {
        var userName = req.UserName?.Trim() ?? string.Empty;
        var email = req.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = req.Password ?? string.Empty;
        var normalizedUserName = userName.ToLowerInvariant();

        if (userName.Length < UserInputLimits.UserNameMinLength)
        {
            AddError(r => r.UserName, "User name must be at least 3 characters.");
        }
        else if (userName.Length > UserInputLimits.UserNameMaxLength)
        {
            AddError(r => r.UserName, $"User name must be at most {UserInputLimits.UserNameMaxLength} characters.");
        }

        if (password.Length < UserInputLimits.PasswordMinLength)
        {
            AddError(r => r.Password, "Password must be at least 8 characters.");
        }
        else if (password.Length > UserInputLimits.PasswordMaxLength)
        {
            AddError(r => r.Password, $"Password must be at most {UserInputLimits.PasswordMaxLength} characters.");
        }

        if (email.Length > UserInputLimits.EmailMaxLength ||
            email.Length < 3 ||
            !email.Contains('@'))
        {
            AddError(r => r.Email, "Email is invalid.");
        }

        if (ValidationFailed)
        {
            await SendErrorsAsync(400, ct);
            return;
        }

        var exists = dbContext.Database.IsRelational()
            ? await dbContext.Users.AnyAsync(
                u => EF.Property<string>(u, "NormalizedEmail") == email ||
                     EF.Property<string>(u, "NormalizedUserName") == normalizedUserName,
                ct)
            : await dbContext.Users.AnyAsync(
                u => u.Email.ToLower() == email || u.UserName.ToLower() == normalizedUserName,
                ct);
        if (exists)
        {
            AddError("A user with the same email or user name already exists.");
            await SendErrorsAsync(409, ct);
            return;
        }

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            PasswordHash = hasher.HashPassword(null!, password),
            Role = UserRole.User,
            EmailVerifiedAt = emailVerification.IsEnabled ? null : DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUserUniquenessConflict(ex))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            AddError("A user with the same email or user name already exists.");
            await SendErrorsAsync(409, ct);
            return;
        }

        var verification = await emailVerification.SendForRegistrationAsync(user, ct);
        await SendAsync(new RegisterResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            RequiresEmailVerification = verification.Required,
            VerificationEmailSent = verification.Sent
        }, 201, ct);
    }

    internal static bool IsUserUniquenessConflict(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraintName
            })
        {
            return false;
        }

        return constraintName.Equals("ix_users_email", StringComparison.Ordinal) ||
               constraintName.Equals("ix_users_username", StringComparison.Ordinal);
    }
}
