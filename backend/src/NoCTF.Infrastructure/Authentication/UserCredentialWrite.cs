using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

/// <summary>All credential replacements compare the observed hash; invalidation increments in PostgreSQL.</summary>
internal static class UserCredentialWrite
{
    private static readonly AsyncKeyedLock.AsyncKeyedLocker<Guid> DevelopmentLocks = new();
    public static async Task InvalidateResetTokensAsync(NoCtfDbContext db, Guid userId, Guid? exceptId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = db.AccountTokens.Where(token => token.UserId == userId && token.Kind == AccountTokenKind.PasswordReset
            && (exceptId == null || token.Id != exceptId) && token.ConsumedAt == null && token.InvalidatedAt == null);
        if (db.Database.IsRelational()) await tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.InvalidatedAt, now), ct);
        else foreach (var token in await tokens.ToArrayAsync(ct)) token.InvalidatedAt = now;
    }
    public static Task InvalidateTokensAsync(NoCtfDbContext db, User trackedUser, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        trackedUser.TokenVersion = checked(trackedUser.TokenVersion + 1);
        return Task.CompletedTask;
    }
    public static async Task<bool> ReplaceAsync(NoCtfDbContext db, User observed, string passwordHash,
        bool invalidateTokens, DateTimeOffset now, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            // This development-only lock is not a relational concurrency implementation.
            using var lease = await DevelopmentLocks.LockAsync(observed.Id, ct);
            var current = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == observed.Id
                && user.Kind == UserKind.Human && user.AccountStatus == UserAccountStatus.Active, ct);
            if (current is null || current.PasswordHash != observed.PasswordHash) return false;
            var tracked = db.Users.Local.FirstOrDefault(user => user.Id == observed.Id);
            if (tracked is null) { tracked = current; db.Users.Attach(tracked); }
            tracked.PasswordHash = passwordHash;
            tracked.TokenVersion = checked(current.TokenVersion + (invalidateTokens ? 1 : 0));
            tracked.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return true;
        }
        return await db.Users.Where(user => user.Id == observed.Id && user.PasswordHash == observed.PasswordHash
                && user.Kind == UserKind.Human && user.AccountStatus == UserAccountStatus.Active)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PasswordHash, passwordHash)
                .SetProperty(user => user.TokenVersion, user => user.TokenVersion + (invalidateTokens ? 1 : 0))
                .SetProperty(user => user.UpdatedAt, now), ct) == 1;
    }
}
