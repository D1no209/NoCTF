using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionConfigurationStore(NoCtfDbContext db) : ICompetitionConfigurationStore
{
    public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken ct) =>
        (from config in db.CompetitionConfigurations.AsNoTracking()
         join competition in db.Competitions.AsNoTracking() on config.CompetitionId equals competition.Id
         where config.CompetitionId == competitionId && !competition.Deletion.IsDeleted
         select new CompetitionConfigurationView(config.CompetitionId, config.Mode, config.Json, config.Revision,
             competition.Status, config.UpdatedAt)).SingleOrDefaultAsync(ct);

    public async Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        int expectedRevision,
        string json,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return new(null, CompetitionConfigurationUpdateFailure.CompetitionNotFound);
        if (status is CompetitionStatus.Paused or CompetitionStatus.Finished
            || status == CompetitionStatus.Running && !allowWhileRunning)
            return new(null, CompetitionConfigurationUpdateFailure.ConfigurationLocked);
        var changed = await db.CompetitionConfigurations
            .Where(x => x.CompetitionId == competitionId && x.Revision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Json, json)
                .SetProperty(x => x.Revision, expectedRevision + 1)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (changed != 1) return new(null, CompetitionConfigurationUpdateFailure.RevisionConflict);
        await transaction.CommitAsync(ct);
        return new(await FindAsync(competitionId, ct));
    }
}
