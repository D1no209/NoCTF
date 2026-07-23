using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionLifecycleStore(NoCtfDbContext db) : ICompetitionLifecycleStore
{
    public Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => (CompetitionStatus?)item.Status)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await db.Competitions.AsNoTracking()
            .Where(item => item.DeletedAt == null
                && ((item.Status == CompetitionStatus.Published && item.StartAt <= now)
                    || (item.Status != CompetitionStatus.Draft
                        && item.Status != CompetitionStatus.Finished
                        && item.EndAt <= now)))
            .Select(item => new CompetitionLifecycleSnapshot(item.Id, item.Status, item.StartAt, item.EndAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryTransitionAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        CancellationToken cancellationToken)
    {
        var changed = await db.Competitions
            .Where(item => item.Id == competitionId && item.Status == from)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, to)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        return changed == 1;
    }

    public async Task<bool> TryTransitionWithAuditAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        Guid? actorId,
        string? reason,
        bool automatic,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var lockedStatus = await CompetitionWriteLock.AcquireAsync(
            db, competitionId, cancellationToken);
        if (lockedStatus != from)
            return false;
        var competition = await db.Competitions
            .Include(item => item.LifecycleAudits)
            .SingleAsync(item => item.Id == competitionId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (from == CompetitionStatus.Running && competition.RunningSince is { } runningSince)
        {
            competition.AccumulatedRunningSeconds = checked(
                competition.AccumulatedRunningSeconds
                + (long)Math.Floor((now - runningSince).TotalSeconds));
            competition.RunningSince = null;
        }
        if (to == CompetitionStatus.Running)
            competition.RunningSince = now;
        competition.Status = to;
        competition.UpdatedAt = now;
        competition.LifecycleAudits.Add(new CompetitionLifecycleAudit
        {
            Id = Guid.CreateVersion7(now),
            From = from,
            To = to,
            ActorId = actorId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Automatic = automatic,
            OccurredAt = now
        });
        competition.LeaderboardRevision = checked(competition.LeaderboardRevision + 1);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
