using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloViewerStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer, TimeProvider clock) : ILiveSoloViewerStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    public Task<LiveSoloViewerResult> EnterAsync(Guid competitionId, Guid matchId, Guid actorId, Guid? existingLeaseId, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (!await LiveSoloViewerEligibility.AllowedAsync(db, authorizer, competitionId, matchId, actorId, ct))
                return new(null, LiveSoloViewerFailure.Unavailable);
            var now = clock.GetUtcNow();
            var userId = actorId == Guid.Empty ? (Guid?)null : actorId;
            var existing = existingLeaseId is Guid id ? await db.Set<LiveSoloViewerLease>().SingleOrDefaultAsync(x => x.Id == id
                && x.CompetitionId == competitionId && x.MatchId == matchId && x.UserId == userId && x.ExpiresAt > now, ct) : null;
            if (existing is not null)
            {
                if (!await LiveSoloViewerEligibility.WithinCapacityAsync(db, competitionId, existing.Id, now, ct))
                { db.Remove(existing); return new(null, LiveSoloViewerFailure.CapacityReached); }
                existing.ExpiresAt = now + Lifetime; return new(new(existing.Id, existing.ExpiresAt));
            }
            var expired = await db.Set<LiveSoloViewerLease>().Where(x => x.CompetitionId == competitionId && x.ExpiresAt <= now).ToArrayAsync(ct);
            db.RemoveRange(expired);
            var maximum = await db.Set<LiveSoloCompetitionModeConfiguration>().Where(x => x.CompetitionId == competitionId).Select(x => x.MaximumViewers).SingleAsync(ct);
            if (await db.Set<LiveSoloViewerLease>().CountAsync(x => x.CompetitionId == competitionId && x.ExpiresAt > now, ct) >= maximum)
                return new(null, LiveSoloViewerFailure.CapacityReached);
            var lease = new LiveSoloViewerLease { Id = Guid.CreateVersion7(now), CompetitionId = competitionId, MatchId = matchId, UserId = userId, ExpiresAt = now + Lifetime };
            db.Add(lease); return new(new(lease.Id, lease.ExpiresAt));
        }, ct);

    public Task<LiveSoloViewerResult> RenewAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var userId = actorId == Guid.Empty ? (Guid?)null : actorId;
            var lease = await db.Set<LiveSoloViewerLease>().SingleOrDefaultAsync(x => x.Id == leaseId && x.CompetitionId == competitionId
                && x.MatchId == matchId && x.UserId == userId, ct);
            if (lease is null || lease.ExpiresAt <= clock.GetUtcNow()) return new(null, LiveSoloViewerFailure.Expired);
            if (!await LiveSoloViewerEligibility.AllowedAsync(db, authorizer, competitionId, matchId, actorId, ct))
            { db.Remove(lease); return new(null, LiveSoloViewerFailure.Unavailable); }
            if (!await LiveSoloViewerEligibility.WithinCapacityAsync(db, competitionId, leaseId, clock.GetUtcNow(), ct))
            { db.Remove(lease); return new(null, LiveSoloViewerFailure.CapacityReached); }
            lease.ExpiresAt = clock.GetUtcNow() + Lifetime; return new(new(lease.Id, lease.ExpiresAt));
        }, ct);
    public async Task LeaveAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct)
    {
        await TransactionAsync(async () =>
        {
            var userId = actorId == Guid.Empty ? (Guid?)null : actorId;
            var lease = await db.Set<LiveSoloViewerLease>().SingleOrDefaultAsync(x => x.Id == leaseId && x.CompetitionId == competitionId
                && x.MatchId == matchId && x.UserId == userId, ct);
            if (lease is not null) db.Remove(lease);
            return new(null);
        }, ct);
    }
    private async Task<LiveSoloViewerResult> TransactionAsync(Func<Task<LiveSoloViewerResult>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
            }
            catch (Exception ex) when (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); if (attempt >= 2) return new(null, LiveSoloViewerFailure.Conflict); }
        }
    }
}
