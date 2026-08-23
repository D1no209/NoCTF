using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Challenges.Configuration;

public sealed class ChallengeConfigurationStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : IChallengeConfigurationStore
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
                item.Configuration.RulesJson,
                item.Competition.ConfigurationJson,
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
        string json,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new { candidate.Mode })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var published = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == challengeId
                && challenge.CompetitionId == competitionId
                && challenge.DeletedAt == null)
            .Select(challenge => (bool?)challenge.IsPublished)
            .SingleOrDefaultAsync(ct);
        if (published is null)
            return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        if (db.Database.IsRelational())
        {
            var changed = await db.CompetitionChallenges
                .Where(configuration =>
                    configuration.Id == challengeId
                    && configuration.CompetitionId == competitionId
                    && configuration.DeletedAt == null
                    )
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(configuration => configuration.RulesJson, json)
                    .SetProperty(configuration => configuration.UpdatedAt, updatedAt), ct);
            if (changed != 1)
                return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        }
        else
        {
            var tracked = await db.CompetitionChallenges.SingleOrDefaultAsync(
                configuration => configuration.Id == challengeId
                    && configuration.CompetitionId == competitionId
                    && configuration.DeletedAt == null, ct);
            if (tracked is not null)
            {
                tracked.RulesJson = json;
                tracked.UpdatedAt = updatedAt;
            }
            if (tracked is null)
                return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        }
        await LeaderboardDirty.MarkAsync(db, competitionId, ct);
        if (competition.Mode == GameMode.Awd
            && status == CompetitionStatus.Running
            && published.Value)
        {
            if (db.Database.IsRelational())
            {
                await db.RuntimeInstances
                    .Where(runtime => runtime.CompetitionId == competitionId
                        && runtime.CompetitionChallengeId == challengeId
                        && runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(runtime => runtime.CheckerSequence, runtime => runtime.CheckerSequence + 1)
                        .SetProperty(runtime => runtime.LastAppliedCheckerSequence, runtime => runtime.CheckerSequence + 1)
                        .SetProperty(runtime => runtime.CheckerDeadlineAt, (DateTimeOffset?)null)
                        .SetProperty(runtime => runtime.NextCheckerDueAt, updatedAt), ct);
            }
            else
            {
                var runtimes = await db.RuntimeInstances
                    .Where(runtime => runtime.CompetitionId == competitionId
                        && runtime.CompetitionChallengeId == challengeId
                        && runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running)
                    .ToListAsync(ct);
                foreach (var runtime in runtimes)
                {
                    runtime.CheckerSequence += 1;
                    runtime.LastAppliedCheckerSequence = runtime.CheckerSequence;
                    runtime.CheckerDeadlineAt = null;
                    runtime.NextCheckerDueAt = updatedAt;
                }
            }
            await outbox.PublishAsync(new AdvanceAwdRound(
                competitionId,
                challengeId,
                updatedAt));
        }
        if (!db.Database.IsRelational())
            await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await FindAsync(competitionId, challengeId, ct));
    }
}
