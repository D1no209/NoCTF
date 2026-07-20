using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeConfigurationStore(NoCtfDbContext db) : IChallengeConfigurationStore
{
    public Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct) =>
        (from configuration in db.ChallengeConfigurations.AsNoTracking()
         join challenge in db.Challenges.AsNoTracking() on configuration.ChallengeId equals challenge.Id
         join competition in db.Competitions.AsNoTracking() on challenge.CompetitionId equals competition.Id
         where challenge.Id == challengeId
               && challenge.CompetitionId == competitionId
               && !challenge.Deletion.IsDeleted
               && !competition.Deletion.IsDeleted
         select new ChallengeConfigurationView(
             competition.Id,
             challenge.Id,
             competition.Mode,
             configuration.Json,
             configuration.Revision,
             competition.Status,
             configuration.UpdatedAt))
        .SingleOrDefaultAsync(ct);

    public async Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        int expectedRevision,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        if (status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return new(null, ChallengeConfigurationUpdateFailure.ConfigurationLocked);
        if (!await db.Challenges.AsNoTracking().AnyAsync(challenge => challenge.Id == challengeId
            && challenge.CompetitionId == competitionId && !challenge.Deletion.IsDeleted, ct))
            return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        var changed = await db.ChallengeConfigurations
            .Where(configuration =>
                configuration.ChallengeId == challengeId
                && configuration.Revision == expectedRevision
                && db.Challenges.Any(challenge =>
                    challenge.Id == configuration.ChallengeId
                    && challenge.CompetitionId == competitionId
                    && !challenge.Deletion.IsDeleted)
                )
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(configuration => configuration.Json, json)
                .SetProperty(configuration => configuration.Revision, expectedRevision + 1)
                .SetProperty(configuration => configuration.UpdatedAt, updatedAt), ct);

        if (changed != 1) return new(null, ChallengeConfigurationUpdateFailure.RevisionConflict);
        await transaction.CommitAsync(ct);
        return new(await FindAsync(competitionId, challengeId, ct));
    }
}
