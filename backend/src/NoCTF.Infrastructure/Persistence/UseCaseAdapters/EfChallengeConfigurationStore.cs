using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeConfigurationStore(NoCtfDbContext db) : IChallengeConfigurationStore
{
    public Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct) =>
        db.CompetitionChallenges.AsNoTracking()
            .Join(db.Challenges.AsNoTracking(), configuration => configuration.ChallengeId, challenge => challenge.Id,
                (configuration, challenge) => new { Configuration = configuration, Challenge = challenge })
            .Join(db.Competitions.AsNoTracking(), item => item.Configuration.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Configuration, item.Challenge, Competition = competition })
            .Where(item => item.Configuration.Id == challengeId
                           && item.Configuration.CompetitionId == competitionId
                           && item.Challenge.DeletedAt == null
                           && item.Competition.DeletedAt == null)
            .Select(item => new ChallengeConfigurationView(
                item.Competition.Id,
                item.Configuration.Id,
                item.Competition.Mode,
                item.Configuration.ConfigurationJson,
                item.Competition.ConfigurationJson,
                item.Competition.ConfigurationRevision,
                item.Configuration.Revision,
                item.Competition.Status,
                db.Teams.Count(team => team.CompetitionId == item.Competition.Id
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned
                    && team.DeletedAt == null),
                item.Configuration.UpdatedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        int expectedRevision,
        int expectedCompetitionConfigurationRevision,
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        if (!await db.Competitions.AsNoTracking().AnyAsync(competition =>
                competition.Id == competitionId
                && competition.ConfigurationRevision == expectedCompetitionConfigurationRevision,
                ct))
            return new(null, ChallengeConfigurationUpdateFailure.RevisionConflict);
        if (!await db.CompetitionChallenges.AsNoTracking().AnyAsync(challenge => challenge.Id == challengeId
            && challenge.CompetitionId == competitionId && challenge.DeletedAt == null, ct))
            return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        var changed = await db.CompetitionChallenges
            .Where(configuration =>
                configuration.Id == challengeId
                && configuration.Revision == expectedRevision
                && configuration.CompetitionId == competitionId
                && configuration.DeletedAt == null
                )
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(configuration => configuration.ConfigurationJson, json)
                .SetProperty(configuration => configuration.Revision, expectedRevision + 1)
                .SetProperty(configuration => configuration.UpdatedAt, updatedAt), ct);

        if (changed != 1) return new(null, ChallengeConfigurationUpdateFailure.RevisionConflict);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        return new(await FindAsync(competitionId, challengeId, ct));
    }
}
