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
                competition.ConfigurationRevision,
                competition.Status,
                db.Teams.Count(team => team.CompetitionId == competition.Id
                    && team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned
                    && team.DeletedAt == null),
                db.CompetitionChallenges
                    .Where(challenge => challenge.CompetitionId == competition.Id && challenge.DeletedAt == null)
                    .OrderBy(challenge => challenge.Id)
                    .Select(challenge => new CompetitionChallengeConfigurationSnapshot(
                        challenge.Id, challenge.Revision, challenge.RulesJson))
                    .ToArray(),
                competition.ConfigurationUpdatedAt))
            .SingleOrDefaultAsync(ct);

    public async Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        int expectedRevision,
        string json,
        bool allowWhileRunning,
        IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
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
        var currentChallengeRevisions = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.CompetitionId == competitionId && challenge.DeletedAt == null)
            .Select(challenge => new { challenge.Id, challenge.Revision })
            .ToListAsync(ct);
        if (currentChallengeRevisions.Count != expectedChallengeRevisions.Count
            || currentChallengeRevisions.Any(challenge =>
                !expectedChallengeRevisions.TryGetValue(challenge.Id, out var revision)
                || revision != challenge.Revision))
            return new(null, CompetitionConfigurationUpdateFailure.RevisionConflict);
        int changed;
        if (db.Database.IsRelational())
        {
            changed = await db.Competitions
                .Where(x => x.Id == competitionId && x.ConfigurationRevision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ConfigurationJson, json)
                    .SetProperty(x => x.ConfigurationRevision, expectedRevision + 1)
                    .SetProperty(x => x.LeaderboardRevision, x => checked(x.LeaderboardRevision + 1))
                    .SetProperty(x => x.ConfigurationUpdatedAt, now), ct);
        }
        else
        {
            // EF Core InMemory does not support ExecuteUpdateAsync; apply the same
            // optimistic-concurrency update through the change tracker.
            var tracked = await db.Competitions.SingleOrDefaultAsync(
                x => x.Id == competitionId && x.ConfigurationRevision == expectedRevision, ct);
            if (tracked is not null)
            {
                tracked.ConfigurationJson = json;
                tracked.ConfigurationRevision = expectedRevision + 1;
                tracked.LeaderboardRevision = checked(tracked.LeaderboardRevision + 1);
                tracked.ConfigurationUpdatedAt = now;
            }
            changed = tracked is null ? 0 : 1;
        }
        if (changed != 1) return new(null, CompetitionConfigurationUpdateFailure.RevisionConflict);
        await outbox.PublishAsync(new InvalidateLeaderboard(competitionId));
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
                .Select(challenge => new { challenge.Id, challenge.Revision })
                .ToListAsync(ct);
            foreach (var challenge in challenges)
            {
                await outbox.PublishAsync(new AdvanceAwdRound(
                    competitionId,
                    challenge.Id,
                    now,
                    checked(expectedRevision + 1),
                    challenge.Revision));
            }
        }
        if (!db.Database.IsRelational())
            await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(await FindAsync(competitionId, ct));
    }
}
