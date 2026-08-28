using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AdministratorBootstrapper(
    NoCtfDbContext db,
    IOptions<SeedAdministratorOptions> configuredOptions,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var options = configuredOptions.Value;
        var password = options.Password;
        if (string.IsNullOrWhiteSpace(password))
            return;

        var userName = options.UserName.Trim();
        var email = EmailCanonicalizer.Canonicalize(options.Email);
        Validate(userName, email, password);

        if (await db.Users.AsNoTracking().AnyAsync(
                user => user.Role == UserRole.Administrator,
                ct))
            return;

        var normalizedUserName = userName.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(
                user => user.NormalizedUserName == normalizedUserName
                    || user.Email == email,
                ct))
            throw new InvalidOperationException(
                "SeedAdmin identity belongs to an existing non-administrator user.");

        var now = timeProvider.GetUtcNow();
        var administrator = new User
        {
            Id = Guid.CreateVersion7(now),
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            Kind = UserKind.Human,
            Role = UserRole.Administrator,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        administrator.PasswordHash = passwordHasher.HashPassword(administrator, password);
        db.Users.Add(administrator);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(string userName, string email, string password)
    {
        if (userName.Length is < 3 or > 64
            || !userName.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-'))
            throw new InvalidOperationException("SeedAdmin:UserName is invalid.");
        if (email.Length > 320
            || !MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SeedAdmin:Email is invalid.");
        if (password.Length is < 8 or > 1024)
            throw new InvalidOperationException("SeedAdmin:Password must contain 8 to 1024 characters.");
    }
}
