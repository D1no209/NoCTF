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

    public async Task<ChallengeConfigurationView?> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        int expectedRevision,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        var changed = await db.ChallengeConfigurations
            .Where(configuration =>
                configuration.ChallengeId == challengeId
                && configuration.Revision == expectedRevision
                && db.Challenges.Any(challenge =>
                    challenge.Id == configuration.ChallengeId
                    && challenge.CompetitionId == competitionId
                    && !challenge.Deletion.IsDeleted)
                && db.Competitions.Any(competition =>
                    competition.Id == competitionId
                    && !competition.Deletion.IsDeleted
                    && (competition.Status == CompetitionStatus.Draft
                        || competition.Status == CompetitionStatus.Published)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(configuration => configuration.Json, json)
                .SetProperty(configuration => configuration.Revision, expectedRevision + 1)
                .SetProperty(configuration => configuration.UpdatedAt, updatedAt), ct);

        return changed == 1
            ? await FindAsync(competitionId, challengeId, ct)
            : null;
    }
}
