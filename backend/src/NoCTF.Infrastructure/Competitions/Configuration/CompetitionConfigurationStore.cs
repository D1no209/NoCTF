using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Competitions.Configuration;

public sealed class CompetitionConfigurationStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : ICompetitionConfigurationStore
{
    public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId && competition.DeletedAt == null)
            .Select(competition => new CompetitionConfigurationView(
                competition.Id,
                competition.Mode,
                competition.ConfigurationJson,
                competition.Status,
                db.Teams.Count(team => team.CompetitionId == competition.Id
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned
                    && team.DeletedAt == null),
                db.CompetitionChallenges
                    .Where(challenge => challenge.CompetitionId == competition.Id && challenge.DeletedAt == null)
                    .OrderBy(challenge => challenge.Id)
                    .Select(challenge => new CompetitionChallengeConfigurationSnapshot(
                        challenge.Id, challenge.RulesJson))
                    .ToArray(),
                competition.ConfigurationUpdatedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        string json,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return new(null, CompetitionConfigurationUpdateFailure.CompetitionNotFound);
        var mode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        int changed;
        if (db.Database.IsRelational())
        {
            changed = await db.Competitions
                .Where(x => x.Id == competitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ConfigurationJson, json)
                    .SetProperty(x => x.LeaderboardDirty, true)
                    .SetProperty(x => x.ConfigurationUpdatedAt, now), ct);
        }
        else
        {
            // EF Core InMemory does not support ExecuteUpdateAsync; apply the same
            // last-write-wins update through the change tracker.
            var tracked = await db.Competitions.SingleOrDefaultAsync(
                x => x.Id == competitionId, ct);
            if (tracked is not null)
            {
                tracked.ConfigurationJson = json;
                tracked.LeaderboardDirty = true;
                tracked.ConfigurationUpdatedAt = now;
            }
            changed = tracked is null ? 0 : 1;
        }
        if (changed != 1) return new(null, CompetitionConfigurationUpdateFailure.CompetitionNotFound);
        if (mode == GameMode.Awd && status == CompetitionStatus.Running)
        {
            if (db.Database.IsRelational())
            {
                await db.RuntimeInstances
                    .Where(runtime => runtime.CompetitionId == competitionId
                        && runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(runtime => runtime.CheckerSequence, runtime => runtime.CheckerSequence + 1)
                        .SetProperty(runtime => runtime.LastAppliedCheckerSequence, runtime => runtime.CheckerSequence + 1)
                        .SetProperty(runtime => runtime.CheckerDeadlineAt, (DateTimeOffset?)null)
                        .SetProperty(runtime => runtime.NextCheckerDueAt, now), ct);
            }
            else
            {
                var runtimes = await db.RuntimeInstances
                    .Where(runtime => runtime.CompetitionId == competitionId
                        && runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running)
                    .ToListAsync(ct);
                foreach (var runtime in runtimes)
                {
                    runtime.CheckerSequence += 1;
                    runtime.LastAppliedCheckerSequence = runtime.CheckerSequence;
                    runtime.CheckerDeadlineAt = null;
                    runtime.NextCheckerDueAt = now;
                }
            }
            var challenges = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == competitionId
                    && challenge.IsPublished
                    && challenge.DeletedAt == null)
                .Select(challenge => challenge.Id)
                .ToListAsync(ct);
            foreach (var challengeId in challenges)
            {
                await outbox.PublishAsync(new AdvanceAwdRound(
                    competitionId,
                    challengeId,
                    now));
            }
        }
        if (!db.Database.IsRelational())
            await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await FindAsync(competitionId, ct));
    }
}
