using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using System.Text.Json;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

public sealed class CompetitionLifecycleStore(
    NoCtfDbContext db,
    CompetitionStartGate startGate,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null)
    : ICompetitionLifecycleStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

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
        if (!db.Database.IsRelational())
        {
            var competition = await db.Competitions.SingleOrDefaultAsync(
                item => item.Id == competitionId && item.Status == from,
                cancellationToken);
            if (competition is null)
                return false;
            competition.Status = to;
            competition.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (readModels is not null)
                await readModels.InvalidateAsync(competitionId, cancellationToken);
            return true;
        }
        var changed = await db.Competitions
            .Where(item => item.Id == competitionId && item.Status == from)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, to)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        if (changed == 1 && readModels is not null)
            await readModels.InvalidateAsync(competitionId, cancellationToken);
        return changed == 1;
    }

    public async Task<bool> TryTransitionWithAuditAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        Guid? actorId,
        string? reason,
        bool automatic,
        CompetitionLifecycleEffects effects,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var lockedStatus = await CompetitionStateReader.ReadAsync(
            db, competitionId, cancellationToken);
        if (lockedStatus != from)
            return false;
        if (from == CompetitionStatus.Published && to == CompetitionStatus.Running
            && (await startGate.ValidateAsync(competitionId, cancellationToken)) is not { Count: 0 })
            return false;
        var competition = await db.Competitions
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
        {
            competition.RunningSince = now.AddTicks(
                -(now.Ticks % TimeSpan.TicksPerMicrosecond));
        }
        if (competition.Mode == GameMode.Awd)
        {
            if (from == CompetitionStatus.Running && to == CompetitionStatus.Paused)
            {
                await db.RuntimeInstances
                    .Where(instance => instance.CompetitionId == competitionId
                        && instance.NextCheckerDueAt != null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                instance => instance.NextCheckerDueAt,
                                (DateTimeOffset?)null)
                            .SetProperty(
                                instance => instance.CheckerSequence,
                                instance => instance.CheckerSequence + 1)
                            .SetProperty(
                                instance => instance.LastAppliedCheckerSequence,
                                instance => instance.CheckerSequence + 1)
                            .SetProperty(
                                instance => instance.CheckerDeadlineAt,
                                (DateTimeOffset?)null),
                        cancellationToken);
            }
            else if (from == CompetitionStatus.Paused && to == CompetitionStatus.Running)
            {
                await db.RuntimeInstances
                    .Where(instance => instance.CompetitionId == competitionId
                        && instance.State == NoCTF.Domain.Runtime.RuntimeState.Running)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            instance => instance.NextCheckerDueAt,
                            now),
                        cancellationToken);
                var pauseEvent = await db.CompetitionEvents.AsNoTracking()
                    .Where(@event => @event.CompetitionId == competitionId
                        && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
                    .OrderByDescending(@event => @event.OccurredAt)
                    .ThenByDescending(@event => @event.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                var pauseStartedAt = pauseEvent is not null
                    && JsonSerializer.Deserialize<LifecyclePayload>(pauseEvent.PayloadJson, JsonOptions)
                        is { From: CompetitionStatus.Running, To: CompetitionStatus.Paused }
                    ? pauseEvent.OccurredAt
                    : (DateTimeOffset?)null;
                if (pauseStartedAt is DateTimeOffset pausedAt)
                {
                    var pauseDuration = now - pausedAt;
                    var currentFlags = await db.ChallengeFlags
                        .Join(
                            db.CompetitionChallenges,
                            flag => flag.CompetitionChallengeId,
                            challenge => (Guid?)challenge.Id,
                            (flag, challenge) => new { Flag = flag, Challenge = challenge })
                        .Where(item => item.Challenge.CompetitionId == competitionId
                            && item.Flag.SpecificationKind == SpecificationKind.AwdRound
                            && item.Flag.ValidStart <= pausedAt
                            && item.Flag.ValidUntil > pausedAt
                            && item.Flag.DeletedAt == null)
                        .Select(item => item.Flag)
                        .ToListAsync(cancellationToken);
                    foreach (var flag in currentFlags)
                        flag.ValidUntil = flag.ValidUntil!.Value.Add(pauseDuration);
                }
            }
        }
        if (to == CompetitionStatus.Finished
            && competition.LeaderboardVisibility != CompetitionLeaderboardVisibility.Normal)
        {
            var effectiveVisibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                competition.Status,
                competition.LeaderboardVisibility,
                competition.LeaderboardVisibilityStartsAt,
                now);
            if (effectiveVisibility != CompetitionLeaderboardVisibility.Normal)
            {
                await events.RecordAsync(new(
                    competition.Id,
                    CompetitionEventKind.LeaderboardVisibilityChanged,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Public,
                    now,
                    ActorUserId: actorId,
                    CompetitionStatus: to,
                    LeaderboardVisibility: CompetitionLeaderboardVisibility.Normal,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        from = effectiveVisibility,
                        to = CompetitionLeaderboardVisibility.Normal,
                        dataCutoffAt = now,
                        automatic = true,
                        reason = "competition_finished"
                    })),
                    cancellationToken);
            }
            competition.LeaderboardVisibility = CompetitionLeaderboardVisibility.Normal;
            competition.LeaderboardVisibilityStartsAt = null;
            competition.LeaderboardVisibilityAppliedAt = now;
            competition.LeaderboardVisibilityRevision = checked(
                competition.LeaderboardVisibilityRevision + 1);
            competition.FrozenLeaderboardSnapshotJson = null;
        }
        competition.Status = to;
        competition.UpdatedAt = now;
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.CompetitionLifecycleChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            now,
            ActorUserId: actorId,
            CompetitionStatus: to,
            PayloadJson: JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                competitionStatus = to,
                from,
                to,
                automatic,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            })), cancellationToken);
        await LeaderboardRevision.IncrementAsync(
            db,
            competitionId,
            cancellationToken);
        await outbox.PublishAsync(new InvalidateLeaderboard(competitionId));
        if (effects.HasFlag(CompetitionLifecycleEffects.ProvisionRuntimes))
            await outbox.PublishAsync(new ProvisionCompetitionRuntimes(competitionId));
        if (effects.HasFlag(CompetitionLifecycleEffects.CleanupRuntimes))
            await outbox.PublishAsync(new CleanupCompetitionRuntimes(competitionId));
        if (competition.Mode == GameMode.Awd && to == CompetitionStatus.Running)
        {
            var challenges = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == competitionId
                    && challenge.IsPublished
                    && challenge.DeletedAt == null)
                .Select(challenge => new { challenge.Id, challenge.Revision })
                .ToListAsync(cancellationToken);
            foreach (var challenge in challenges)
            {
                await outbox.PublishAsync(new AdvanceAwdRound(
                    competitionId,
                    challenge.Id,
                    now,
                    competition.ConfigurationRevision,
                    challenge.Revision));
            }
        }
        if (competition.Mode == GameMode.Koh && to == CompetitionStatus.Running)
        {
            var challenges = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == competitionId
                    && challenge.IsPublished
                    && challenge.DeletedAt == null)
                .Select(challenge => new { challenge.Id, challenge.Revision })
                .ToListAsync(cancellationToken);
            foreach (var challenge in challenges)
            {
                await outbox.PublishAsync(new PollKohChallenge(
                    competitionId,
                    challenge.Id,
                    competition.ConfigurationRevision,
                    challenge.Revision,
                    competition.RunningSince!.Value,
                    now));
            }
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
        }
        if (readModels is not null)
            await readModels.InvalidateAsync(competitionId, cancellationToken);
        return true;
    }

    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);
}
