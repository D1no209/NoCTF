using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AuthenticationStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher) : IUserAuthenticationStore
{
    public async Task<AuthenticatedUser?> FindByLoginAsync(
        string login,
        CancellationToken ct)
    {
        var normalized = login.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Kind == UserKind.Human
                && (item.NormalizedEmail == normalized
                || item.NormalizedUserName == normalized
                || item.Id.ToString() == login),
            ct);
        return ToAuthenticated(user);
    }

    public async Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human)
            return false;
        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return result != PasswordVerificationResult.Failed;
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken ct) =>
        ToAuthenticated(await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userId, ct));

    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfile(
                user.Id,
                user.UserName,
                user.Email,
                user.Role,
                user.Kind,
                user.EmailVerifiedAt != null))
            .SingleOrDefaultAsync(ct);

    public async Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalizedUserName = userName.ToUpperInvariant();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return CreateUserState.UserNameConflict;
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, ct))
            return CreateUserState.EmailConflict;

        var user = new User
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Kind = UserKind.Human,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return CreateUserState.Created;
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.AnyAsync(
                existing => existing.NormalizedEmail == normalizedEmail, ct)
                ? CreateUserState.EmailConflict
                : CreateUserState.UserNameConflict;
        }
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null
            || user.Kind != UserKind.Human
            || passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return false;

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IncrementTokenVersionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.TokenVersion, user => user.TokenVersion + 1)
                    .SetProperty(user => user.UpdatedAt, now),
                ct) == 1;

    private static AuthenticatedUser? ToAuthenticated(User? user) =>
        user is null
            ? null
            : new(user.Id, user.UserName, user.Role, user.Kind, user.TokenVersion);
}
