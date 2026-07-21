using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionConfigurationStore(NoCtfDbContext db) : ICompetitionConfigurationStore
{
    public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId && !competition.Deletion.IsDeleted)
            .Select(competition => new CompetitionConfigurationView(
                competition.Id,
                competition.Mode,
                competition.ConfigurationJson,
                competition.ConfigurationRevision,
                competition.Status,
                competition.ConfigurationUpdatedAt))
            .SingleOrDefaultAsync(ct);

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
        var changed = await db.Competitions
            .Where(x => x.Id == competitionId && x.ConfigurationRevision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ConfigurationJson, json)
                .SetProperty(x => x.ConfigurationRevision, expectedRevision + 1)
                .SetProperty(x => x.ConfigurationUpdatedAt, now), ct);
        if (changed != 1) return new(null, CompetitionConfigurationUpdateFailure.RevisionConflict);
        await transaction.CommitAsync(ct);
        return new(await FindAsync(competitionId, ct));
    }
}
