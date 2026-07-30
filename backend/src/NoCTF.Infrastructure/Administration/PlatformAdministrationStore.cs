using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;
using Wolverine.Persistence.Durability;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAdministrationStore(
    NoCtfDbContext db,
    WolverineProcessDeadLetters deadLetters,
    IPasswordHasher<User> passwordHasher) : IPlatformAdministrationStore
{
    public async Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
            .ToListAsync(ct);

    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalizedUserName = userName.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return new(CreateBotState.UserNameConflict);

        var id = Guid.CreateVersion7(now);
        var email = BotIdentity.DummyEmail(id);
        var user = new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Kind = UserKind.Bot,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Guid.NewGuid().ToString("N"));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(CreateBotState.Created, Map(user));
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return new(CreateBotState.UserNameConflict);
        }
    }

    public async Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return null;
        if (user.Kind == UserKind.Bot && role != UserRole.Organizer)
            return null;
        user.Role = role;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return null;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken ct)
    {
        return (await deadLetters.ListAsync(limit, ct)).Select(Map).ToArray();
    }

    public async Task<DeadLetterView?> FindDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        var result = await deadLetters.FindAsync(messageId, ct);
        return result is null ? null : Map(result);
    }

    public async Task<bool> RequeueDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        return await deadLetters.ReplayAsync(messageId, ct);
    }

    private static PlatformUserView Map(User user) =>
        new(
            user.Id, user.UserName, user.Email, user.Kind, user.Role, user.TokenVersion,
            user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt);

    private static DeadLetterView Map(DeadLetterEnvelope envelope) =>
        new(
            envelope.Id,
            envelope.MessageType,
            envelope.Source,
            envelope.ExceptionType,
            envelope.SentAt,
            envelope.Replayable);
}
