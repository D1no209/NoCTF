using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Visibility;

public sealed class CompetitionVisibilityStore(
    NoCtfDbContext db,
    ILeaderboardSnapshotFactory snapshots,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null)
    : ICompetitionVisibilityStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<CompetitionVisibilityConfigurationView?> GetAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .SingleOrDefaultAsync(ct);
        return competition is null ? null : View(competition, now);
    }

    public async Task<CompetitionVisibilityMutationResult> UpdateAsync(
        UpdateCompetitionVisibilityCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        if (await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct) is null)
            return new(CompetitionVisibilityMutationState.NotFound);

        var competition = await db.Competitions
            .SingleAsync(candidate => candidate.Id == command.CompetitionId, ct);
        if (competition.LeaderboardVisibilityRevision != command.ExpectedRevision)
        {
            return new(
                CompetitionVisibilityMutationState.RevisionConflict,
                View(competition, command.Now));
        }
        var validationFailure = CompetitionVisibilityRules.Validate(
            competition.Status,
            competition.StartAt,
            competition.EndAt,
            command);
        if (validationFailure is { } state)
            return new(state, View(competition, command.Now));

        var before = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.Status,
            competition.LeaderboardVisibility,
            competition.LeaderboardVisibilityStartsAt,
            command.Now);
        var scheduled = command.Visibility != CompetitionLeaderboardVisibility.Normal
            && command.StartsAt is not null;
        DateTimeOffset? startsAt = command.Visibility == CompetitionLeaderboardVisibility.Normal
            ? null
            : TruncateToMicroseconds(command.StartsAt ?? command.Now);
        var revision = checked(competition.LeaderboardVisibilityRevision + 1);
        var after = scheduled
            ? CompetitionLeaderboardVisibility.Normal
            : command.Visibility;
        string? frozenSnapshotJson = null;
        if (!scheduled && command.Visibility == CompetitionLeaderboardVisibility.Frozen)
        {
            var snapshot = await snapshots.CreateAsync(
                competition.Id,
                startsAt!.Value,
                historical: true,
                ct);
            if (snapshot is null)
                return new(CompetitionVisibilityMutationState.NotFound);
            var nextLeaderboardRevision = checked(competition.LeaderboardRevision + 1);
            frozenSnapshotJson = JsonSerializer.Serialize(snapshot with
            {
                Visibility = CompetitionLeaderboardVisibility.Frozen,
                DataScope = LeaderboardDataScope.Frozen,
                DataAsOf = startsAt,
                SnapshotRevision = nextLeaderboardRevision,
                TargetRevision = nextLeaderboardRevision,
                Stale = false,
                LastFailureAt = null
            }, JsonOptions);
        }

        competition.LeaderboardVisibility = command.Visibility;
        competition.LeaderboardVisibilityStartsAt = startsAt;
        competition.LeaderboardVisibilityAppliedAt = scheduled ? null : command.Now;
        competition.LeaderboardVisibilityRevision = revision;
        competition.FrozenLeaderboardSnapshotJson = frozenSnapshotJson;
        competition.UpdatedAt = command.Now;

        if (!scheduled && (before != after || after == CompetitionLeaderboardVisibility.Frozen))
        {
            db.Set<CompetitionLeaderboardVisibilityAudit>().Add(new()
            {
                Id = Guid.CreateVersion7(command.Now),
                CompetitionId = competition.Id,
                From = before,
                To = after,
                DataCutoffAt = after == CompetitionLeaderboardVisibility.Frozen ? startsAt : null,
                ActorId = command.ActorId,
                Reason = NormalizeReason(command.Reason),
                Automatic = false,
                OccurredAt = command.Now
            });
            await events.RecordAsync(new(
                competition.Id,
                CompetitionEventKind.LeaderboardVisibilityChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                command.Now,
                ActorUserId: command.ActorId,
                CompetitionStatus: competition.Status,
                LeaderboardVisibility: after), ct);
        }

        if (scheduled)
        {
            await outbox.ScheduleAsync(
                new ApplyCompetitionVisibility(competition.Id, revision),
                startsAt!.Value);
        }
        else if (before != after || after == CompetitionLeaderboardVisibility.Frozen)
        {
            await LeaderboardRevision.IncrementAsync(db, competition.Id, ct);
            await outbox.PublishAsync(new InvalidateLeaderboard(competition.Id));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        if (readModels is not null)
            await readModels.InvalidateAsync(competition.Id, ct);
        return new(
            CompetitionVisibilityMutationState.Updated,
            View(competition, command.Now));
    }

    public async Task ApplyScheduledAsync(
        Guid competitionId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, ct) is null)
            return;
        var competition = await db.Competitions
            .SingleAsync(candidate => candidate.Id == competitionId, ct);
        if (competition.LeaderboardVisibilityRevision != expectedRevision
            || competition.LeaderboardVisibility == CompetitionLeaderboardVisibility.Normal
            || competition.LeaderboardVisibilityAppliedAt is not null
            || competition.Status == CompetitionStatus.Finished
            || competition.LeaderboardVisibilityStartsAt is not { } startsAt)
            return;
        if (startsAt > now)
        {
            await outbox.ScheduleAsync(
                new ApplyCompetitionVisibility(competition.Id, expectedRevision),
                startsAt);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        if (competition.LeaderboardVisibility == CompetitionLeaderboardVisibility.Frozen)
        {
            var snapshot = await snapshots.CreateAsync(
                competition.Id,
                startsAt,
                historical: true,
                ct);
            if (snapshot is null)
                return;
            var nextLeaderboardRevision = checked(competition.LeaderboardRevision + 1);
            competition.FrozenLeaderboardSnapshotJson = JsonSerializer.Serialize(snapshot with
            {
                Visibility = CompetitionLeaderboardVisibility.Frozen,
                DataScope = LeaderboardDataScope.Frozen,
                DataAsOf = startsAt,
                SnapshotRevision = nextLeaderboardRevision,
                TargetRevision = nextLeaderboardRevision,
                Stale = false,
                LastFailureAt = null
            }, JsonOptions);
        }

        competition.LeaderboardVisibilityAppliedAt = now;
        competition.UpdatedAt = now;
        db.Set<CompetitionLeaderboardVisibilityAudit>().Add(new()
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = competition.Id,
            From = CompetitionLeaderboardVisibility.Normal,
            To = competition.LeaderboardVisibility,
            DataCutoffAt = competition.LeaderboardVisibility == CompetitionLeaderboardVisibility.Frozen
                ? startsAt
                : null,
            Reason = "scheduled_visibility_started",
            Automatic = true,
            OccurredAt = now
        });
        await events.RecordAsync(new(
            competition.Id,
            CompetitionEventKind.LeaderboardVisibilityChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            now,
            CompetitionStatus: competition.Status,
            LeaderboardVisibility: competition.LeaderboardVisibility), ct);
        await LeaderboardRevision.IncrementAsync(db, competition.Id, ct);
        await outbox.PublishAsync(new InvalidateLeaderboard(competition.Id));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        if (readModels is not null)
            await readModels.InvalidateAsync(competition.Id, ct);
    }

    private static CompetitionVisibilityConfigurationView View(
        Competition competition,
        DateTimeOffset now) =>
        new(
            competition.Id,
            competition.Status,
            competition.StartAt,
            competition.EndAt,
            competition.LeaderboardVisibility,
            CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                competition.Status,
                competition.LeaderboardVisibility,
                competition.LeaderboardVisibilityStartsAt,
                now),
            competition.LeaderboardVisibilityStartsAt,
            competition.LeaderboardVisibilityAppliedAt,
            competition.LeaderboardVisibilityRevision);

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
