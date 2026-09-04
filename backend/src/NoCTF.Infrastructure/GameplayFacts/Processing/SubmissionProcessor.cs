using NoCTF.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Challenges;
using System.Globalization;
using System.Text.Json;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class GameplayFactProcessor(
    NoCtfDbContext db,
    IGameplayFactEvaluatorCatalog evaluatorCatalog,
    IGameplayFactAdmissionModePolicy admissionModePolicy,
    ITransactionalMessageOutbox outbox,
    ILeaderboardSnapshotFactory leaderboardSnapshots,
    BloodRankCriticalSection bloodRankCriticalSection,
    TeamChallengeCriticalSection teamChallengeCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null,
    ILogger<GameplayFactProcessor>? logger = null,
    TimeProvider? clock = null) : IGameplayFactProcessor
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public GameplayFactProcessor(
        NoCtfDbContext db,
        IGameplayFactEvaluatorCatalog evaluatorCatalog,
        IGameplayFactAdmissionModePolicy admissionModePolicy,
        ITransactionalMessageOutbox outbox,
        ILeaderboardSnapshotFactory leaderboardSnapshots,
        ICompetitionEventRecorder? eventRecorder = null,
        ILogger<GameplayFactProcessor>? logger = null)
        : this(
            db,
            evaluatorCatalog,
            admissionModePolicy,
            outbox,
            leaderboardSnapshots,
            new BloodRankCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            new TeamChallengeCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder,
            logger)
    { }

    private static readonly CompetitionEventKind[] CheatResolutionKinds =
    [
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected
    ];

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly ILogger<GameplayFactProcessor> log =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameplayFactProcessor>.Instance;

    public async Task ProcessAsync(
        Guid gameplayFactId,
        CancellationToken cancellationToken)
    {
        var claimedGameplayFactId = await ClaimAsync(gameplayFactId, cancellationToken);
        if (claimedGameplayFactId is null)
            return;

        var evaluation = await EvaluateAsync(claimedGameplayFactId.Value, cancellationToken);
        await CompleteAsync(claimedGameplayFactId.Value, evaluation, cancellationToken);
    }

    private async Task<Guid?> ClaimAsync(
        Guid requestedGameplayFactId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var requested = await db.GameplayFacts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == requestedGameplayFactId, cancellationToken);
        if (requested is null || requested.State != GameplayFactState.Queued)
            return null;
        var scope = await ResolveProcessingScopeAsync(requested, cancellationToken);
        using var processingLease = await AcquireProcessingScopeAsync(
            scope, cancellationToken);
        if (await ApplyProcessingScope(db.GameplayFacts.AsNoTracking(), scope)
                .AnyAsync(item => item.State == GameplayFactState.Processing, cancellationToken))
            return null;
        var submission = await ApplyProcessingScope(db.GameplayFacts, scope)
            .Where(item => item.State == GameplayFactState.Queued)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (submission is null)
            return null;
        submission.State = GameplayFactState.Processing;
        submission.UpdatedAt = timeProvider.GetUtcNow();
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return submission.Id;
    }

    private async Task<Evaluation> EvaluateAsync(Guid gameplayFactId, CancellationToken cancellationToken)
    {
        var submission = await db.GameplayFacts.AsNoTracking()
            .SingleAsync(item => item.Id == gameplayFactId, cancellationToken);
        var configuration = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == submission.CompetitionChallengeId),
                competition => competition.Id,
                challenge => challenge.CompetitionId,
                (competition, challenge) => new
                {
                    Competition = competition,
                    CompetitionChallenge = challenge
                })
            .Join(
                db.Challenges.AsNoTracking(),
                scope => scope.CompetitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (scope, challenge) => new
                {
                    scope.Competition,
                    scope.CompetitionChallenge,
                    Challenge = challenge
                })
            .SingleAsync(cancellationToken);
        var rules = admissionModePolicy.GetRules(
            configuration.Competition.Mode,
            configuration.Competition.ConfigurationJson,
            configuration.CompetitionChallenge.RulesJson);
        if (submission.Kind is GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment)
        {
            var special = await EvaluateSpecialAsync(
                submission,
                configuration.Competition.Status,
                configuration.CompetitionChallenge,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return new(special);
        }
        var maxAttempts = submission.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt
            ? rules.MaxFlagAttempts
            : rules.MaxFixAttempts;
        if (maxAttempts is > 0)
        {
            var acceptedPosition = await db.GameplayFacts.AsNoTracking()
                .Where(candidate =>
                    candidate.CompetitionId == submission.CompetitionId
                    && candidate.TeamId == submission.TeamId
                    && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                    && candidate.Kind == submission.Kind
                    && candidate.State != GameplayFactState.PlatformFailed
                    && (candidate.OccurredAt < submission.OccurredAt
                        || candidate.OccurredAt == submission.OccurredAt
                        && candidate.Id.CompareTo(submission.Id) <= 0))
                .CountAsync(cancellationToken);
            if (acceptedPosition > maxAttempts)
                return new(new(GameplayFactResult.AttemptsExhausted, null, submission.OccurredAt));
        }

        var priorSubmissions = await db.GameplayFacts.AsNoTracking()
            .Where(item =>
                item.CompetitionId == submission.CompetitionId
                && item.CompetitionChallengeId == submission.CompetitionChallengeId
                && item.TeamId == submission.TeamId
                && (item.Result == GameplayFactResult.Correct
                    || item.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected)
                && (item.OccurredAt < submission.OccurredAt
                    || item.OccurredAt == submission.OccurredAt
                    && item.Id.CompareTo(submission.Id) < 0))
            .ToListAsync(cancellationToken);
        var flags = await db.ChallengeFlags.AsNoTracking()
            .Where(flag =>
                flag.CompetitionChallengeId == submission.CompetitionChallengeId
                || flag.ChallengeId == configuration.CompetitionChallenge.ChallengeId)
            .Where(flag => flag.TeamId == null || flag.TeamId == submission.TeamId
                || configuration.Competition.Mode == GameMode.Ctf
                || configuration.Competition.Mode == GameMode.Awd
                || configuration.Competition.Mode == GameMode.Awdp)
            .ToListAsync(cancellationToken);
        var patch = submission.ReferenceKind == GameplayFactReferenceKind.PatchUpload
            && submission.ReferenceId is { } patchUploadId
            ? await db.PatchUploads.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == patchUploadId, cancellationToken)
            : null;
        TimeSpan? effectiveRunningTime = null;
        if (configuration.Competition.Mode == GameMode.Awd)
        {
            var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
                .Where(@event => @event.CompetitionId == submission.CompetitionId
                    && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                    && @event.OccurredAt <= submission.OccurredAt)
                .OrderBy(@event => @event.OccurredAt)
                .ToListAsync(cancellationToken);
            effectiveRunningTime = AwdEffectiveRunningClock.Calculate(
                lifecycleEvents.Select(@event =>
                {
                    var payload = JsonSerializer.Deserialize<LifecyclePayload>(
                        @event.PayloadJson,
                        JsonOptions)!;
                    return new CompetitionLifecycleTransition
                    {
                        Id = @event.Id,
                        CompetitionId = @event.CompetitionId,
                        From = payload.From,
                        To = payload.To,
                        ActorId = @event.ActorUserId,
                        OccurredAt = @event.OccurredAt
                    };
                }).ToList(),
                submission.OccurredAt);
        }
        var decision = evaluatorCatalog.Get(configuration.Competition.Mode).Evaluate(new(
            submission,
            priorSubmissions,
            flags,
            patch,
            configuration.Competition.ConfigurationJson,
            configuration.CompetitionChallenge.RulesJson,
            configuration.Competition.StartAt,
            effectiveRunningTime,
            configuration.Challenge.DefinitionJson));
        return new(decision);
    }

    private async Task<GameplayFactDecision> EvaluateSpecialAsync(
        GameplayFact submission,
        CompetitionStatus competitionStatus,
        NoCTF.Domain.Challenges.CompetitionChallenge challenge,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        if (submission.Kind == GameplayFactKind.ManualAdjustment)
        {
            if (!int.TryParse(
                    submission.Value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var delta)
                || delta == 0
                || delta.ToString(CultureInfo.InvariantCulture) != submission.Value)
            {
                return new(
                    null,
                    GameplayFactFailureCode.HintUnavailable,
                    submission.OccurredAt);
            }
            return new(
                GameplayFactResult.Applied,
                null,
                submission.OccurredAt);
        }

        if (competitionStatus != CompetitionStatus.Running)
            return new(GameplayFactResult.Rejected,
                GameplayFactFailureCode.HintUnavailable, submission.OccurredAt);

        if (submission.ReferenceKind != GameplayFactReferenceKind.Hint
            || submission.ReferenceId is not Guid hintId)
            return new(GameplayFactResult.Rejected,
                GameplayFactFailureCode.HintUnavailable, submission.OccurredAt);
        var hint = challenge.Hints.SingleOrDefault(item =>
            item.Id == hintId
            && item.PublishedAt is not null
            && item.PublishedAt <= submission.OccurredAt
            && item.HiddenAt is null);
        if (hint is null || !challenge.IsPublished)
            return new(GameplayFactResult.Rejected,
                GameplayFactFailureCode.HintUnavailable, submission.OccurredAt);

        var alreadyUnlocked = await db.GameplayFacts.AsNoTracking()
            .Where(item => item.CompetitionId == submission.CompetitionId
                && item.TeamId == submission.TeamId
                && item.Kind == GameplayFactKind.HintUnlock
                && item.Id != submission.Id
                && item.ReferenceKind == GameplayFactReferenceKind.Hint
                && item.ReferenceId == hintId
                && item.Result == GameplayFactResult.Unlocked)
            .AnyAsync(ct);
        if (alreadyUnlocked)
            return new(GameplayFactResult.Duplicate, null, submission.OccurredAt,
                ReferenceKind: GameplayFactReferenceKind.Hint, ReferenceId: hintId);

        var score = await AuthoritativeScoreBeforeSubmissionAsync(
            submission,
            hint.Cost,
            projectedAt,
            ct);
        if (score < hint.Cost)
            return new(GameplayFactResult.Rejected,
                GameplayFactFailureCode.InsufficientScore, submission.OccurredAt,
                ReferenceKind: GameplayFactReferenceKind.Hint, ReferenceId: hintId);
        return new(GameplayFactResult.Unlocked, null, submission.OccurredAt,
            ReferenceKind: GameplayFactReferenceKind.Hint, ReferenceId: hintId);
    }

    private async Task<long> AuthoritativeScoreBeforeSubmissionAsync(
        GameplayFact submission,
        long currentHintCost,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        var snapshot = await leaderboardSnapshots.CreateAsync(
                submission.CompetitionId,
                projectedAt,
                ct)
            ?? throw new InvalidOperationException(
                $"Competition {submission.CompetitionId} has no leaderboard projection.");
        var score = snapshot.Entries
            .SingleOrDefault(entry => entry.TeamId == submission.TeamId)?.Score ?? 0;
        var currentEventContributes = submission.Kind == GameplayFactKind.HintUnlock
            && submission.Result == GameplayFactResult.Unlocked;
        return currentEventContributes
            ? checked(score + currentHintCost)
            : score;
    }

    private async Task CompleteAsync(
        Guid gameplayFactId,
        Evaluation evaluation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var submission = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == gameplayFactId, cancellationToken);
        if (submission is null
            || submission.State != GameplayFactState.Processing)
            return;
        var processingScope = await ResolveProcessingScopeAsync(
            submission, cancellationToken);
        using var processingLease = await AcquireProcessingScopeAsync(
            processingScope, cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (submission.Kind is GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment)
        {
            await AcquireTeamScoringLockAsync(
                submission.CompetitionId,
                submission.TeamId!.Value,
                cancellationToken);
            var specialScope = await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == submission.CompetitionId)
                .Join(
                    db.CompetitionChallenges.AsNoTracking()
                        .Where(challenge => challenge.Id == submission.CompetitionChallengeId),
                    competition => competition.Id,
                    challenge => challenge.CompetitionId,
                    (competition, challenge) => new { Competition = competition, Challenge = challenge })
                .SingleAsync(cancellationToken);
            evaluation = evaluation with
            {
                Decision = await EvaluateSpecialAsync(
                    submission,
                    specialScope.Competition.Status,
                    specialScope.Challenge,
                    now,
                    cancellationToken)
            };
        }

        if (evaluation.Decision.Result is null)
        {
            submission.State = GameplayFactState.PlatformFailed;
            submission.FailureCode =
                evaluation.Decision.FailureCode ?? GameplayFactFailureCode.CheckerPlatformError;
            submission.UpdatedAt = now;
            await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.GameplayFactAdjudicated,
                CompetitionEventLevel.Error,
                CompetitionEventVisibility.Team,
                now,
                ActorUserId: submission.ActorUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                GameplayFactId: submission.Id,
                GameplayFactKind: submission.Kind,
                GameplayFactState: submission.State,
                GameplayFactResult: submission.Result), cancellationToken);
            await RecordAwdpBreakResolutionAsync(submission, now, cancellationToken);
            await QueueNextGameplayFactAsync(processingScope, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var previousGameplayFactResult = submission.Result;
        var previousFailureCode = submission.FailureCode;
        if (submission.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected)
        {
            var resolved = await db.CompetitionEvents.AsNoTracking().AnyAsync(
                @event => (@event.SubjectType == EntityReferenceKind.GameplayFact
                        && @event.SubjectId == submission.Id
                        || @event.RelatedType == EntityReferenceKind.GameplayFact
                        && @event.RelatedId == submission.Id)
                    && CheatResolutionKinds.Contains(@event.Kind),
                cancellationToken);
            if (!resolved)
            {
                await events.RecordAsync(new(
                    submission.CompetitionId,
                    CompetitionEventKind.CheatIncidentSuperseded,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Staff,
                    now,
                    TeamId: submission.TeamId,
                    CompetitionChallengeId: submission.CompetitionChallengeId,
                    GameplayFactId: submission.Id,
                    GameplayFactKind: submission.Kind,
                    GameplayFactResult: submission.Result,
                    Reason: "Superseded by gameplay fact re-evaluation."), cancellationToken);
            }
        }
        submission.VictimTeamId = evaluation.Decision.VictimTeamId;
        submission.Result = evaluation.Decision.Result;
        submission.FailureCode = evaluation.Decision.FailureCode;
        submission.ReferenceKind = evaluation.Decision.ReferenceKind ?? submission.ReferenceKind;
        submission.ReferenceId = evaluation.Decision.ReferenceId ?? submission.ReferenceId;
        submission.State = GameplayFactState.Completed;
        submission.UpdatedAt = now;
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
        if (submission.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected
            && submission.TeamId is Guid sourceTeamId
            && submission.ActorUserId is Guid actorUserId)
        {
            if (submission.VictimTeamId is Guid ownerTeamId)
            {
                await outbox.PublishAsync(new ForeignTeamFlagDetected(
                    submission.CompetitionId,
                    submission.Id,
                    sourceTeamId,
                    ownerTeamId,
                    actorUserId,
                    submission.CompetitionChallengeId,
                    submission.OccurredAt));
            }
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.CheatIncidentDetected,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                submission.OccurredAt,
                ActorUserId: submission.ActorUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                GameplayFactId: submission.Id,
                GameplayFactKind: submission.Kind,
                GameplayFactResult: submission.Result), cancellationToken);
            log.LogWarning(
                "Foreign team Flag detected for competition {CompetitionId}, gameplay fact {GameplayFactId}, source team {SourceTeamId}, owner team {OwnerTeamId}.",
                submission.CompetitionId,
                submission.Id,
                submission.TeamId,
                submission.VictimTeamId);
        }
        else if (submission.FailureCode == GameplayFactFailureCode.AmbiguousFlagMatch)
        {
            log.LogWarning(
                "Ambiguous Flag ownership detected for competition {CompetitionId}, gameplay fact {GameplayFactId}, source team {SourceTeamId}.",
                submission.CompetitionId,
                submission.Id,
                submission.TeamId);
        }
        var bloodAward = previousGameplayFactResult == GameplayFactResult.Correct
            ? null
            : await TryCreateBloodAwardAsync(
                submission,
                evaluation,
                cancellationToken);
        await StopSolvedChallengeRuntimesAsync(
            submission,
            submission,
            now,
            cancellationToken);
        if (bloodAward is not null)
            await outbox.PublishAsync(bloodAward);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            evaluation.Decision.Result is null
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            now,
            ActorUserId: submission.ActorUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            GameplayFactState: submission.State,
            GameplayFactResult: submission.Result), cancellationToken);
        await RecordAwdpBreakResolutionAsync(submission, now, cancellationToken);
        if (bloodAward is not null)
        {
            var bloodKind = bloodAward.BloodRank switch
            {
                LeaderboardBloodRank.First => CompetitionEventKind.FirstBloodAwarded,
                LeaderboardBloodRank.Second => CompetitionEventKind.SecondBloodAwarded,
                LeaderboardBloodRank.Third => CompetitionEventKind.ThirdBloodAwarded,
                _ => throw new InvalidOperationException("Unsupported leaderboard blood rank.")
            };
            await events.RecordAsync(new(
                submission.CompetitionId,
                bloodKind,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                bloodAward.OccurredAt,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                GameplayFactId: submission.Id,
                GameplayFactKind: submission.Kind,
                GameplayFactResult: submission.Result), cancellationToken);
        }
        await QueueNextGameplayFactAsync(processingScope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private async Task RecordAwdpBreakResolutionAsync(
        GameplayFact submission,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (submission.Kind != GameplayFactKind.BreakAttempt)
            return;
        if (submission.FailureCode == GameplayFactFailureCode.DuplicateAchievement)
            return;

        var isAwdp = await db.Competitions
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => competition.Mode == GameMode.Awdp)
            .SingleAsync(ct);
        if (!isAwdp)
            return;

        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.AwdpBreakResolved,
            submission.State == GameplayFactState.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            occurredAt,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            GameplayFactState: submission.State,
            GameplayFactResult: submission.Result), ct);
    }

    private async Task StopSolvedChallengeRuntimesAsync(
        GameplayFact submission,
        GameplayFact evaluatedFact,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (evaluatedFact.Result != GameplayFactResult.Correct)
            return;

        var competitionMode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        var runtimePurpose = (competitionMode, submission.Kind) switch
        {
            (GameMode.Ctf, GameplayFactKind.FlagAttempt) => RuntimePurpose.Player,
            (GameMode.Awdp, GameplayFactKind.BreakAttempt) => RuntimePurpose.AwdpAttack,
            _ => (RuntimePurpose?)null
        };
        if (runtimePurpose is null)
            return;

        await AcquireTeamScoringLockAsync(
            submission.CompetitionId,
            submission.TeamId!.Value,
            ct);
        var runtimes = await db.RuntimeInstances
            .Where(instance =>
                instance.CompetitionId == submission.CompetitionId
                && instance.CompetitionChallengeId == submission.CompetitionChallengeId
                && instance.TeamId == submission.TeamId
                && instance.Purpose == runtimePurpose.Value
                && (instance.RuntimeKind == RuntimeKind.Container
                    || instance.RuntimeKind == RuntimeKind.Compose)
                && (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || instance.State == RuntimeState.Failed
                        && instance.ProviderReceiptJson != null))
            .OrderBy(instance => instance.CreatedAt)
            .ThenBy(instance => instance.Id)
            .ToListAsync(ct);
        foreach (var runtime in runtimes)
        {
            if (runtime.State == RuntimeState.Queued)
            {
                runtime.State = RuntimeState.Stopped;
                runtime.StoppedAt = occurredAt;
            }
            else
            {
                runtime.State = RuntimeState.Stopping;
                await outbox.PublishAsync(new StopRuntime(runtime.Id));
            }

            await events.RecordAsync(new(
                runtime.CompetitionId!.Value,
                CompetitionEventKind.RuntimeStateChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Team,
                occurredAt,
                ActorUserId: submission.ActorUserId,
                TeamId: runtime.TeamId,
                CompetitionChallengeId: runtime.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: submission.Id,
                RuntimeState: runtime.State), ct);
        }
    }

    private Task AcquireTeamScoringLockAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pg_advisory_xact_lock(hashtextextended({competitionId.ToString() + ":" + teamId}, 0))",
            ct);

    private async Task<GameplayFactProcessingScope> ResolveProcessingScopeAsync(
        GameplayFact submission,
        CancellationToken ct)
    {
        var mode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        return new(
            submission.CompetitionId,
            submission.CompetitionChallengeId,
            submission.Kind,
            submission.TeamId,
            submission.TeamId is null
                || mode == GameMode.Ctf && submission.Kind == GameplayFactKind.FlagAttempt);
    }

    private ValueTask<IDisposable> AcquireProcessingScopeAsync(
        GameplayFactProcessingScope scope,
        CancellationToken ct) =>
        scope.ChallengeWide
            ? bloodRankCriticalSection.AcquireAsync(db, scope.CompetitionChallengeId, ct)
            : teamChallengeCriticalSection.AcquireAsync(
                db,
                scope.TeamId!.Value,
                scope.CompetitionChallengeId,
                ct);

    private static IQueryable<GameplayFact> ApplyProcessingScope(
        IQueryable<GameplayFact> facts,
        GameplayFactProcessingScope scope)
    {
        var scoped = facts.Where(item =>
            item.CompetitionId == scope.CompetitionId
            && item.CompetitionChallengeId == scope.CompetitionChallengeId
            && item.Kind == scope.Kind);
        return scope.ChallengeWide
            ? scoped
            : scoped.Where(item => item.TeamId == scope.TeamId);
    }

    private async Task QueueNextGameplayFactAsync(
        GameplayFactProcessingScope scope,
        CancellationToken ct)
    {
        var nextGameplayFactId = await ApplyProcessingScope(
                db.GameplayFacts.AsNoTracking(), scope)
            .Where(item => item.State == GameplayFactState.Queued)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
    }

    private async Task<BloodAwarded?> TryCreateBloodAwardAsync(
        GameplayFact submission,
        Evaluation evaluation,
        CancellationToken ct)
    {
        if (submission.Kind != GameplayFactKind.FlagAttempt
            || evaluation.Decision.Result != GameplayFactResult.Correct)
            return null;

        var competition = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => new
            {
                competition.Mode,
                competition.TrackConfigurationJson
            })
            .SingleAsync(ct);
        if (competition.Mode != GameMode.Ctf)
            return null;

        var tracks = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var currentTrackKey = await db.Teams.AsNoTracking()
            .Where(team => team.Id == submission.TeamId)
            .Select(team => team.TrackKey)
            .SingleAsync(ct);
        if (tracks.Find(currentTrackKey)?.EarnsBlood != true)
            return null;
        var bloodTrackKeys = tracks.Tracks.Where(track => track.EarnsBlood)
            .Select(track => track.Key)
            .ToArray();

        var solvedTeamIds = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == submission.CompetitionId
                && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                && candidate.Kind == GameplayFactKind.FlagAttempt
                && candidate.Result == GameplayFactResult.Correct
                && candidate.Id != submission.Id)
            .Join(
                db.Teams.AsNoTracking().Where(team => bloodTrackKeys.Contains(team.TrackKey)),
                candidate => candidate.TeamId,
                team => (Guid?)team.Id,
                (candidate, _) => candidate.TeamId!.Value)
            .Distinct()
            .ToArrayAsync(ct);
        if (submission.TeamId is Guid teamId && solvedTeamIds.Contains(teamId)
            || solvedTeamIds.Length >= 3)
            return null;

        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == submission.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    Title = challenge.CustomTitle ?? template.Title
                })
            .Join(
                db.Teams.AsNoTracking().Where(team => team.Id == submission.TeamId),
                _ => submission.TeamId,
                team => team.Id,
                (challenge, team) => new { challenge.Title, TeamName = team.Name })
            .SingleAsync(ct);
        return new BloodAwarded(
            submission.CompetitionId,
            submission.CompetitionChallengeId,
            context.Title,
            (LeaderboardBloodRank)(solvedTeamIds.Length + 1),
            submission.TeamId!.Value,
            context.TeamName,
            evaluation.Decision.OccurredAt);
    }

    private sealed record Evaluation(GameplayFactDecision Decision);

    private sealed record GameplayFactProcessingScope(
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        GameplayFactKind Kind,
        Guid? TeamId,
        bool ChallengeWide);

    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);
}
