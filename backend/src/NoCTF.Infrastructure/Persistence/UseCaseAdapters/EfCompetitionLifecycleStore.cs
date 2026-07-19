using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionLifecycleStore(NoCtfDbContext db) : ICompetitionLifecycleStore
{
    public Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId && !item.Deletion.IsDeleted)
            .Select(item => (CompetitionStatus?)item.Status)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await db.Competitions.AsNoTracking()
            .Where(item => !item.Deletion.IsDeleted
                && ((item.Status == CompetitionStatus.Published && item.StartTime <= now)
                    || (item.Status != CompetitionStatus.Draft
                        && item.Status != CompetitionStatus.Finished
                        && item.EndTime <= now)))
            .Select(item => new CompetitionLifecycleSnapshot(item.Id, item.Status, item.StartTime, item.EndTime))
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
}
