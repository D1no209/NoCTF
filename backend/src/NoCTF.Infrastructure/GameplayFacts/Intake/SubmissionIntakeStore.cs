using NoCTF.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

public sealed class GameplayFactIntakeStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    GameplayFactAttemptCriticalSection attemptCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null) : IGameplayFactIntakeStore
{
    public GameplayFactIntakeStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(new LocalCriticalSectionRegistry()),
            eventRecorder) { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        GameplayFactAdmissionPersistence.LoadAsync(
            db, competitionId, competitionChallengeId, userId, cancellationToken);

    public async Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
        FlagGameplayFactReceived received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        var results = await TryAcceptFlagsAsync(
            [received], snapshot, maxAttempts, cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagGameplayFactReceived> received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        if (received.Count == 0)
            return [];
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using var attemptLease = await attemptCriticalSection.AcquireAsync(
            db,
            received[0].TeamId,
            received[0].CompetitionChallengeId,
            received[0].Kind,
            cancellationToken);
        var current = await LoadAdmissionAsync(
            received[0].CompetitionId,
            received[0].CompetitionChallengeId,
            received[0].UserId,
            cancellationToken);
        if (!GameplayFactAdmissionPersistence.Matches(snapshot, current)
            || current!.CompetitionStatus != CompetitionStatus.Running)
            return received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.SnapshotChanged)).ToArray();
        if (maxAttempts is > 0
            && checked(current.AcceptedFlagAttempts + received.Count) > maxAttempts)
            return received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.AttemptsExhausted)).ToArray();

        var entities = received.Select(item => new GameplayFact
        {
            Id = item.GameplayFactId,
            CompetitionId = item.CompetitionId,
            CompetitionChallengeId = item.CompetitionChallengeId,
            TeamId = item.TeamId,
            ActorUserId = item.UserId,
            Kind = item.Kind,
            Value = item.Value,
            ValueSha256 = item.ValueSha256,
            OccurredAt = item.OccurredAt,
            State = GameplayFactState.Queued,
            UpdatedAt = item.OccurredAt
        }).ToArray();
        db.GameplayFacts.AddRange(entities);
        await LeaderboardDirty.MarkAsync(db, received[0].CompetitionId, cancellationToken);
        foreach (var entity in entities)
        {
            await outbox.PublishAsync(new EvaluateGameplayFact(entity.Id));
            await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
            await events.RecordAsync(new(
                entity.CompetitionId,
                CompetitionEventKind.GameplayFactReceived,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                entity.OccurredAt,
                ActorUserId: entity.ActorUserId,
                TeamId: entity.TeamId,
                CompetitionChallengeId: entity.CompetitionChallengeId,
                GameplayFactId: entity.Id,
                GameplayFactKind: entity.Kind,
                GameplayFactState: entity.State), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return entities.Select(entity => new GameplayFactAcceptanceResult(
            GameplayFactAcceptanceState.Created,
            entity.Id,
            entity.OccurredAt)).ToArray();
    }

    public async Task<GameplayFactAcceptanceResult> TryAcceptFixAsync(
        FixGameplayFactReceived received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using var attemptLease = await attemptCriticalSection.AcquireAsync(
            db,
            received.TeamId, received.CompetitionChallengeId, GameplayFactKind.FixAttempt, cancellationToken);
        var current = await LoadAdmissionAsync(
            received.CompetitionId, received.CompetitionChallengeId, received.UserId, cancellationToken);
        if (!GameplayFactAdmissionPersistence.Matches(snapshot, current)
            || current!.CompetitionStatus != CompetitionStatus.Running)
            return new(GameplayFactAcceptanceState.SnapshotChanged);
        if (maxAttempts is > 0 && current.AcceptedFixAttempts >= maxAttempts)
            return new(GameplayFactAcceptanceState.AttemptsExhausted);

        var patch = await db.PatchUploads
            .FromSqlInterpolated($"SELECT * FROM patch_uploads WHERE id = {received.PatchUploadId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (patch is null
            || patch.CompetitionId != received.CompetitionId
            || patch.CompetitionChallengeId != received.CompetitionChallengeId
            || patch.TeamId != received.TeamId
            || patch.UploadedByUserId != received.UserId
            || await db.GameplayFacts.AnyAsync(fact =>
                fact.ReferenceKind == GameplayFactReferenceKind.PatchUpload
                && fact.ReferenceId == patch.Id, cancellationToken))
            return new(GameplayFactAcceptanceState.PatchUploadUnavailable);

        var entity = new GameplayFact
        {
            Id = received.GameplayFactId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            ActorUserId = received.UserId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patch.Id,
            OccurredAt = received.OccurredAt,
            State = GameplayFactState.Queued,
            UpdatedAt = received.OccurredAt
        };
        db.GameplayFacts.Add(entity);
        await LeaderboardDirty.MarkAsync(db, entity.CompetitionId, cancellationToken);
        await outbox.PublishAsync(new EvaluateGameplayFact(entity.Id));
        await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
        await events.RecordAsync(new(
            entity.CompetitionId,
            CompetitionEventKind.GameplayFactReceived,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            entity.OccurredAt,
            ActorUserId: entity.ActorUserId,
            TeamId: entity.TeamId,
            CompetitionChallengeId: entity.CompetitionChallengeId,
            GameplayFactId: entity.Id,
            GameplayFactKind: entity.Kind,
            GameplayFactState: entity.State), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt);
    }

    public async Task<GameplayFactAcceptanceResult> TryAcceptHintUnlockAsync(
        HintUnlockGameplayFactReceived received,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var valid = await db.CompetitionChallenges.AnyAsync(item =>
            item.Id == received.CompetitionChallengeId
            && item.CompetitionId == received.CompetitionId, cancellationToken)
            && await db.Teams.AnyAsync(item =>
                item.Id == received.TeamId
                && item.CompetitionId == received.CompetitionId
                && item.MemberIds.Contains(received.UserId), cancellationToken);
        if (!valid)
            return new(GameplayFactAcceptanceState.SnapshotChanged);
        var entity = new GameplayFact
        {
            Id = received.GameplayFactId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            ActorUserId = received.UserId,
            Kind = GameplayFactKind.HintUnlock,
            ReferenceKind = GameplayFactReferenceKind.Hint,
            ReferenceId = received.HintId,
            OccurredAt = received.OccurredAt,
            State = GameplayFactState.Queued,
            UpdatedAt = received.OccurredAt
        };
        db.GameplayFacts.Add(entity);
        await LeaderboardDirty.MarkAsync(db, entity.CompetitionId, cancellationToken);
        await outbox.PublishAsync(new EvaluateGameplayFact(entity.Id));
        await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt);
    }

    public async Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(
        ManualAdjustmentGameplayFactReceived received,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var valid = await db.CompetitionChallenges.AnyAsync(item =>
            item.Id == received.CompetitionChallengeId
            && item.CompetitionId == received.CompetitionId, cancellationToken)
            && await db.Teams.AnyAsync(item =>
                item.Id == received.TeamId
                && item.CompetitionId == received.CompetitionId, cancellationToken);
        if (!valid)
            return new(GameplayFactAcceptanceState.SnapshotChanged);
        var entity = new GameplayFact
        {
            Id = received.GameplayFactId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            ActorUserId = received.UserId,
            Kind = GameplayFactKind.ManualAdjustment,
            Value = received.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OccurredAt = received.OccurredAt,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Applied,
            UpdatedAt = received.OccurredAt
        };
        db.GameplayFacts.Add(entity);
        await LeaderboardDirty.MarkAsync(db, entity.CompetitionId, cancellationToken);
        await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt);
    }
}
