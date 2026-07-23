using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class PlatformAdministrationStore(
    NoCtfDbContext db,
    IDeadLetters deadLetters) : IPlatformAdministrationStore
{
    public async Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Role, user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
            .ToListAsync(ct);

    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Role, user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
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
        var result = await deadLetters.QueryAsync(new DeadLetterEnvelopeQuery
        {
            PageNumber = 1,
            PageSize = limit
        }, ct);
        return result.Envelopes.Select(Map).ToArray();
    }

    public async Task<DeadLetterView?> FindDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        var result = await deadLetters.QueryAsync(
            new DeadLetterEnvelopeQuery([messageId]) { PageNumber = 1, PageSize = 1 },
            ct);
        return result.Envelopes.Count == 0 ? null : Map(result.Envelopes[0]);
    }

    public async Task<bool> RequeueDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        if (await FindDeadLetterAsync(messageId, ct) is null)
            return false;
        await deadLetters.ReplayAsync(new DeadLetterEnvelopeQuery([messageId]), ct);
        return true;
    }

    private static PlatformUserView Map(User user) =>
        new(
            user.Id, user.UserName, user.Email, user.Role, user.TokenVersion,
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
