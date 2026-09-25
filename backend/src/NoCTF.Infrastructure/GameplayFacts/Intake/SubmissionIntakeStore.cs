using NoCTF.Infrastructure.Persistence;
using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Commands;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Observability;
using NoCTF.GameModes.Registration;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

public sealed class GameplayFactIntakeStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    GameplayFactAttemptCriticalSection attemptCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Application.Authentication.Privacy.IRequestSourceAddress? source = null,
    IRequestReplay? replay = null,
    IChallengeRuntimeTemplateCatalog? runtimeTemplates = null,
    TimeProvider? clock = null,
    IGameplayFactAdmissionModePolicy? admissionModePolicy = null) : IGameplayFactIntakeStore
{
    public GameplayFactIntakeStore(
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            outbox,
            new GameplayFactAttemptCriticalSection(),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly IChallengeRuntimeTemplateCatalog templates =
        runtimeTemplates ?? new ChallengeRuntimeTemplateCatalog();
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly IGameplayFactAdmissionModePolicy modePolicy =
        admissionModePolicy ?? new GameModeGameplayFactAdmissionPolicy();

    public Task<GameplayFactAcceptanceResult[]?> FindFlagReplayAsync(Guid competitionId, Guid challengeId, Guid userId,
        IReadOnlyList<string> flags, CancellationToken ct) => replay is null
            ? Task.FromResult<GameplayFactAcceptanceResult[]?>(null)
            : replay.FindAsync<GameplayFactAcceptanceResult[]>(new(userId, ReplayOperation.FlagSubmission, competitionId, challengeId),
                new FlagReplayFingerprint(flags), ct);

    public async Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = NoCtfTelemetry.ActivitySource.StartActivity("GameplayFact.AdmissionLoad");
        try
        {
            return await GameplayFactAdmissionPersistence.LoadAsync(
                db,
                competitionId,
                competitionChallengeId,
                userId,
                timeProvider.GetUtcNow(),
                templates,
                cancellationToken);
        }
        finally
        {
            NoCtfTelemetry.RecordGameplayFactStage(
                GameplayFactPerformanceStage.AdmissionLoad,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

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
        using var attemptLease = await attemptCriticalSection.AcquireAsync(
            db,
            received[0].TeamId,
            received[0].CompetitionChallengeId,
            received[0].Kind,
            cancellationToken);
        var previous = await FindFlagReplayAsync(received[0].CompetitionId, received[0].CompetitionChallengeId,
            received[0].UserId, received.Select(item => item.Value).ToArray(), cancellationToken);
        if (previous is not null) return previous;
        var recheckStarted = Stopwatch.GetTimestamp();
        GameplayFactAdmissionSnapshot? current;
        using (NoCtfTelemetry.ActivitySource.StartActivity("GameplayFact.AdmissionRecheck"))
        {
            try
            {
                current = await GameplayFactAdmissionPersistence.LoadAsync(
                    db,
                    received[0].CompetitionId,
                    received[0].CompetitionChallengeId,
                    received[0].UserId,
                    timeProvider.GetUtcNow(),
                    templates,
                    cancellationToken);
            }
            finally
            {
                NoCtfTelemetry.RecordGameplayFactStage(
                    GameplayFactPerformanceStage.AdmissionRecheck,
                    Stopwatch.GetElapsedTime(recheckStarted).TotalSeconds);
            }
        }
        if (current is not null
            && current.Mode == GameMode.Awdp
            && received[0].Kind == GameplayFactKind.BreakAttempt
            && current.HasCorrectBreak)
        {
            return received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.AchievementAlreadySucceeded)).ToArray();
        }
        var practice = current is not null
            && GameplayFactAdmissionPolicy.IsPracticeFlagAttempt(
                current,
                received[0].Kind,
                received[0].OccurredAt);
        var currentRules = current is null
            ? null
            : modePolicy.GetRules(current.Mode, current.CompetitionConfiguration,
                current.ChallengeRules, current.ChallengeDefinition);
        var expectedRules = modePolicy.GetRules(snapshot.Mode, snapshot.CompetitionConfiguration,
            snapshot.ChallengeRules, snapshot.ChallengeDefinition);
        if (!GameplayFactAdmissionPersistence.Matches(snapshot, current)
            || currentRules is null
            || currentRules.AllowsFlag != expectedRules.AllowsFlag
            || currentRules.MaxFlagAttempts != expectedRules.MaxFlagAttempts
            || current!.CompetitionStatus != CompetitionStatus.Running && !practice)
            return received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.AdmissionRejected)).ToArray();
        if (!practice && maxAttempts is > 0
            && checked(current.AcceptedFlagAttempts + received.Count) > maxAttempts)
            return received.Select(_ => new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.AttemptsExhausted)).ToArray();

        var entities = received.Select(item =>
        {
            var entity = GameplayFactGeneratedCatalog.Create(item.Kind);
            entity.Id = item.GameplayFactId;
            entity.CompetitionId = item.CompetitionId;
            entity.CompetitionChallengeId = item.CompetitionChallengeId;
            entity.TeamId = item.TeamId;
            entity.ActorUserId = item.UserId;
            entity.SourceIpAddress = source?.Address;
            entity.Value = item.Value;
            entity.ValueSha256 = item.ValueSha256;
            entity.OccurredAt = item.OccurredAt;
            entity.State = GameplayFactState.Queued;
            entity.UpdatedAt = item.OccurredAt;
            return entity;
        }).ToArray();
        db.GameplayFacts.AddRange(entities);
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
            if (current.Mode == GameMode.Awdp
                && entity.Kind == GameplayFactKind.BreakAttempt)
            {
                await events.RecordAsync(new(
                    entity.CompetitionId,
                    CompetitionEventKind.AwdpBreakAttempted,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Public,
                    entity.OccurredAt,
                    TeamId: entity.TeamId,
                    CompetitionChallengeId: entity.CompetitionChallengeId,
                    GameplayFactId: entity.Id,
                    GameplayFactKind: entity.Kind), cancellationToken);
            }
        }
        var response = entities.Select(entity => new GameplayFactAcceptanceResult(
            GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt)).ToArray();
        replay?.Store(response);
        var commitStarted = Stopwatch.GetTimestamp();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        NoCtfTelemetry.RecordGameplayFactStage(
            GameplayFactPerformanceStage.PersistenceCommit,
            Stopwatch.GetElapsedTime(commitStarted).TotalSeconds);
        NoCtfTelemetry.RecordGameplayFactSubmissions(
            entities[0].Kind,
            entities.LongLength);
        var publishStarted = Stopwatch.GetTimestamp();
        await outbox.FlushCommittedMessagesAsync();
        NoCtfTelemetry.RecordGameplayFactStage(
            GameplayFactPerformanceStage.MessagePublish,
            Stopwatch.GetElapsedTime(publishStarted).TotalSeconds);
        return entities.Select(entity => new GameplayFactAcceptanceResult(
            GameplayFactAcceptanceState.Created,
            entity.Id,
            entity.OccurredAt)).ToArray();
    }

    public async Task<GameplayFactAcceptanceResult> TryAcceptHintUnlockAsync(
        HintUnlockGameplayFactReceived received,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var valid = await db.Competitions.AnyAsync(item =>
            item.Id == received.CompetitionId
            && item.Status == CompetitionStatus.Running, cancellationToken)
            && await db.CompetitionChallenges.AnyAsync(item =>
            item.Id == received.CompetitionChallengeId
            && item.CompetitionId == received.CompetitionId, cancellationToken)
            && await db.Teams.AnyAsync(item =>
                item.Id == received.TeamId
                && item.CompetitionId == received.CompetitionId
                && item.Members.Any(member => member.UserId == received.UserId), cancellationToken);
        if (!valid)
            return new(GameplayFactAcceptanceState.AdmissionRejected);
        var entity = new HintUnlockGameplayFact
        {
            Id = received.GameplayFactId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            ActorUserId = received.UserId,
            ReferenceKind = GameplayFactReferenceKind.Hint,
            ReferenceId = received.HintId,
            OccurredAt = received.OccurredAt,
            State = GameplayFactState.Queued,
            UpdatedAt = received.OccurredAt
        };
        db.GameplayFacts.Add(entity);
        await outbox.PublishAsync(new EvaluateGameplayFact(entity.Id));
        await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt);
    }

    public async Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(
        ManualAdjustmentGameplayFactReceived received,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        using var attemptLease = await attemptCriticalSection.AcquireAsync(db, received.TeamId,
            received.CompetitionChallengeId, GameplayFactKind.ManualAdjustment, cancellationToken);
        var previous = replay is null ? null : await replay.FindAsync<GameplayFactAcceptanceResult>(
            new(received.UserId, ReplayOperation.ManualAdjustment, received.CompetitionId, received.CompetitionChallengeId),
            new ManualAdjustmentReplayFingerprint(received.TeamId, received.Delta), cancellationToken);
        if (previous is not null) return previous;
        var valid = await db.CompetitionChallenges.AnyAsync(item =>
            item.Id == received.CompetitionChallengeId
            && item.CompetitionId == received.CompetitionId, cancellationToken)
            && await db.Teams.AnyAsync(item =>
                item.Id == received.TeamId
                && item.CompetitionId == received.CompetitionId, cancellationToken);
        if (!valid)
            return new(GameplayFactAcceptanceState.AdmissionRejected);
        var entity = new ManualAdjustmentGameplayFact
        {
            Id = received.GameplayFactId,
            CompetitionId = received.CompetitionId,
            CompetitionChallengeId = received.CompetitionChallengeId,
            TeamId = received.TeamId,
            ActorUserId = received.UserId,
            Value = received.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OccurredAt = received.OccurredAt,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Applied,
            UpdatedAt = received.OccurredAt
        };
        db.GameplayFacts.Add(entity);
        await outbox.PublishAsync(new GameplayFactStateChanged(entity.Id, entity.State));
        await events.RecordAsync(new(
            entity.CompetitionId,
            CompetitionEventKind.ScoringRecorded,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            entity.OccurredAt,
            ActorUserId: entity.ActorUserId,
            TeamId: entity.TeamId,
            CompetitionChallengeId: entity.CompetitionChallengeId,
            GameplayFactId: entity.Id,
            GameplayFactKind: entity.Kind,
            GameplayFactState: entity.State,
            GameplayFactResult: entity.Result), cancellationToken);
        replay?.Store(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(GameplayFactAcceptanceState.Created, entity.Id, entity.OccurredAt);
    }
}
