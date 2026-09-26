using NoCTF.Infrastructure.Persistence;
using System.Data;
using System.Diagnostics;
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
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class GameplayFactProcessor(
    NoCtfDbContext db,
    IGameplayFactEvaluatorCatalog evaluatorCatalog,
    IGameplayFactAdmissionModePolicy admissionModePolicy,
    IPostCommitMessagePublisher outbox,
    ILeaderboardSnapshotFactory leaderboardSnapshots,
    BloodRankCriticalSection bloodRankCriticalSection,
    TeamChallengeCriticalSection teamChallengeCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null,
    ILogger<GameplayFactProcessor>? logger = null,
    TimeProvider? clock = null,
    ProgressionReconciler? progressionReconciler = null) : IGameplayFactProcessor
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public GameplayFactProcessor(
        NoCtfDbContext db,
        IGameplayFactEvaluatorCatalog evaluatorCatalog,
        IGameplayFactAdmissionModePolicy admissionModePolicy,
        IPostCommitMessagePublisher outbox,
        ILeaderboardSnapshotFactory leaderboardSnapshots,
        ICompetitionEventRecorder? eventRecorder = null,
        ILogger<GameplayFactProcessor>? logger = null)
        : this(
            db,
            evaluatorCatalog,
            admissionModePolicy,
            outbox,
            leaderboardSnapshots,
            new BloodRankCriticalSection(),
            new TeamChallengeCriticalSection(),
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
        Guid? claimedGameplayFactId = null;
        for (var attempt = 0; attempt < 3 && claimedGameplayFactId is null; attempt++)
        {
            try
            {
                claimedGameplayFactId = await ClaimAsync(
                    gameplayFactId,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException
                || TransactionFailureClassifier.IsRetryable(exception))
            {
                db.ChangeTracker.Clear();
            }

            if (claimedGameplayFactId is null && attempt < 2)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(15, 51)),
                    cancellationToken);
            }
        }
        if (claimedGameplayFactId is null)
            return;

        log.LogDebug("Evaluating GameplayFact {GameplayFactId}.", claimedGameplayFactId.Value);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var evaluationStarted = Stopwatch.GetTimestamp();
                Evaluation evaluation;
                using (NoCtfTelemetry.ActivitySource.StartActivity("GameplayFact.WorkerEvaluation"))
                {
                    try
                    {
                        evaluation = await EvaluateAsync(
                            claimedGameplayFactId.Value, cancellationToken);
                    }
                    finally
                    {
                        NoCtfTelemetry.RecordGameplayFactStage(
                            GameplayFactPerformanceStage.WorkerEvaluation,
                            Stopwatch.GetElapsedTime(evaluationStarted).TotalSeconds);
                    }
                }
                log.LogDebug("GameplayFact {GameplayFactId} evaluated with {Result}.",
                    claimedGameplayFactId.Value, evaluation.Decision.Result);
                var completionStarted = Stopwatch.GetTimestamp();
                using (NoCtfTelemetry.ActivitySource.StartActivity("GameplayFact.WorkerCompletion"))
                {
                    try
                    {
                        await CompleteAsync(claimedGameplayFactId.Value, evaluation, cancellationToken);
                    }
                    finally
                    {
                        NoCtfTelemetry.RecordGameplayFactStage(
                            GameplayFactPerformanceStage.WorkerCompletion,
                            Stopwatch.GetElapsedTime(completionStarted).TotalSeconds);
                    }
                }
                log.LogDebug("GameplayFact {GameplayFactId} completion persisted.",
                    claimedGameplayFactId.Value);
                return;
            }
            catch (Exception exception) when (attempt < 2
                && TransactionFailureClassifier.IsRetryable(exception))
            {
                log.LogWarning(exception,
                    "Retrying GameplayFact {GameplayFactId} after transient persistence failure ({Attempt}/3).",
                    claimedGameplayFactId.Value, attempt + 1);
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(
                    Random.Shared.Next(25, 76) * (attempt + 1)), cancellationToken);
            }
        }
        throw new InvalidOperationException("GameplayFact evaluation retry loop did not complete.");
    }

    private async Task<Guid?> ClaimAsync(
        Guid requestedGameplayFactId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var requested = await db.GameplayFacts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == requestedGameplayFactId, cancellationToken);
        if (requested is null)
            return null;
        if (requested.State == GameplayFactState.Queued)
            NoCtfTelemetry.RecordGameplayFactStage(
                GameplayFactPerformanceStage.DispatchAge,
                (timeProvider.GetUtcNow() - requested.OccurredAt).TotalSeconds);
        if (requested.State == GameplayFactState.Processing)
            return requested.Id;
        if (requested.State != GameplayFactState.Queued)
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
            .AsSplitQuery()
            .SingleAsync(cancellationToken);
        var rules = admissionModePolicy.GetRules(
            configuration.Competition.Mode,
            configuration.Competition.ModeConfiguration!,
            configuration.CompetitionChallenge.Rules!,
            configuration.Challenge.Definition);
        var officialWindow = configuration.Competition.Mode == GameMode.Ctf
            ? await CompetitionOfficialWindowReader.ReadAsync(
                db,
                configuration.Competition.Id,
                configuration.Competition.StartAt,
                configuration.Competition.EndAt,
                cancellationToken)
            : (CompetitionOfficialWindow?)null;
        var practiceFact = officialWindow is { } ctfWindow
            && submission.OccurredAt >= ctfWindow.EndAt;
        if (submission.Kind is GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment)
        {
            var special = await EvaluateSpecialAsync(
                submission,
                configuration.Competition.Status,
                configuration.CompetitionChallenge,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return new(special, officialWindow);
        }
        var maxAttempts = practiceFact
            ? null
            : submission.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt
                ? rules.MaxFlagAttempts
                : rules.MaxFixAttempts;
        if (maxAttempts is > 0)
        {
            var acceptedQuery = db.GameplayFacts.AsNoTracking()
                .Where(candidate =>
                    candidate.CompetitionId == submission.CompetitionId
                    && candidate.TeamId == submission.TeamId
                    && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                    && candidate.Kind == submission.Kind
                    && candidate.State != GameplayFactState.PlatformFailed
                    && (candidate.OccurredAt < submission.OccurredAt
                        || candidate.OccurredAt == submission.OccurredAt
                        && candidate.Id.CompareTo(submission.Id) <= 0));
            if (officialWindow is { } attemptWindow)
            {
                acceptedQuery = practiceFact
                    ? acceptedQuery.Where(candidate => candidate.OccurredAt >= attemptWindow.EndAt)
                    : acceptedQuery.Where(candidate => candidate.OccurredAt >= attemptWindow.StartAt
                        && candidate.OccurredAt < attemptWindow.EndAt);
            }
            var acceptedPosition = await acceptedQuery.CountAsync(cancellationToken);
            if (acceptedPosition > maxAttempts)
                return new(
                    new(GameplayFactResult.AttemptsExhausted, null, submission.OccurredAt),
                    officialWindow);
        }

        var priorQuery = db.GameplayFacts.AsNoTracking()
            .Where(item =>
                item.CompetitionId == submission.CompetitionId
                && item.CompetitionChallengeId == submission.CompetitionChallengeId
                && item.TeamId == submission.TeamId
                && (item.Result == GameplayFactResult.Correct
                    || item.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected)
                && (item.OccurredAt < submission.OccurredAt
                    || item.OccurredAt == submission.OccurredAt
                    && item.Id.CompareTo(submission.Id) < 0));
        if (officialWindow is { } priorWindow)
        {
            priorQuery = practiceFact
                ? priorQuery.Where(item => item.OccurredAt >= priorWindow.EndAt)
                : priorQuery.Where(item => item.OccurredAt >= priorWindow.StartAt
                    && item.OccurredAt < priorWindow.EndAt);
        }
        var priorSubmissions = await priorQuery.ToListAsync(cancellationToken);
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
                    return new CompetitionLifecycleTransition
                    {
                        Id = @event.Id,
                        CompetitionId = @event.CompetitionId,
                        From = @event.PreviousCompetitionStatus
                            ?? throw new InvalidOperationException("Lifecycle event has no previous status."),
                        To = @event.CompetitionStatus
                            ?? throw new InvalidOperationException("Lifecycle event has no current status."),
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
            configuration.Competition.ModeConfiguration!,
            configuration.CompetitionChallenge.Rules!,
            configuration.Competition.StartAt,
            effectiveRunningTime,
            configuration.Challenge.Definition));
        return new(decision, officialWindow);
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
        var projection = await leaderboardSnapshots.CreateScoreboardAsync(
                submission.CompetitionId,
                projectedAt,
                ct)
            ?? throw new InvalidOperationException(
                $"Competition {submission.CompetitionId} has no leaderboard projection.");
        var score = projection.Snapshot.Teams
            .SingleOrDefault(entry => entry.TeamId == submission.TeamId)?.TotalScore ?? 0;
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
            IsolationLevel.Serializable, cancellationToken);
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
        var firstAdjudication = submission.Result is null
            && submission.FailureCode is null;
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
                .AsSplitQuery()
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
            if (firstAdjudication)
                RecordProcessingTelemetry(submission, now);
            await outbox.FlushCommittedMessagesAsync();
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
        var announceBlood = false;
        CompetitionLeaderboardVisibility? bloodVisibility = null;
        DateTimeOffset? bloodFrozenAt = null;
        if (bloodAward is not null)
        {
            var visibilityTimes = await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == submission.CompetitionId)
                .Select(competition => new
                {
                    competition.FrozenStartAt,
                    competition.HiddenStartAt
                })
                .SingleAsync(cancellationToken);
            bloodFrozenAt = visibilityTimes.FrozenStartAt;
            bloodVisibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                visibilityTimes.FrozenStartAt,
                visibilityTimes.HiddenStartAt,
                bloodAward.OccurredAt);
            announceBlood = bloodVisibility != CompetitionLeaderboardVisibility.Blackout
                && CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                    visibilityTimes.FrozenStartAt,
                    visibilityTimes.HiddenStartAt,
                    now);
        }
        await StopSolvedChallengeRuntimesAsync(
            submission,
            submission,
            now,
            cancellationToken);
        if (announceBlood && bloodAward is not null)
            await outbox.PublishAsync(bloodAward);
        var adjudicationEventId = await events.RecordAsync(new(
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
        if (announceBlood && bloodAward is not null)
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
                GameplayFactResult: submission.Result,
                LeaderboardVisibility: bloodVisibility,
                FrozenStartAt: bloodFrozenAt,
                ParentEventId: adjudicationEventId == Guid.Empty ? null : adjudicationEventId), cancellationToken);
        }
        await QueueNextGameplayFactAsync(processingScope, cancellationToken);
        await (progressionReconciler ?? new ProgressionReconciler(db))
            .ReconcileCompletedFactAsync(submission, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (firstAdjudication)
            RecordProcessingTelemetry(submission, now);
        await outbox.FlushCommittedMessagesAsync();
    }

    private static void RecordProcessingTelemetry(
        GameplayFact submission,
        DateTimeOffset completedAt) =>
        NoCtfTelemetry.RecordGameplayFactProcessing(
            submission.Kind,
            submission.State,
            submission.Result,
            (completedAt - submission.OccurredAt).TotalSeconds);

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
                // Judge completion/redelivery can arrive after a reset or a fresh start.
                // Only instances already created at submission intake belong to this solve.
                && instance.CreatedAt <= submission.OccurredAt
                && (instance.RuntimeKind == RuntimeKind.Container
                    || instance.RuntimeKind == RuntimeKind.Compose)
                && (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || instance.State == RuntimeState.Failed
                        && instance.ProviderReceipt != null))
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

    private async Task AcquireTeamScoringLockAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        if (!await db.Teams.AsNoTracking().AnyAsync(
                team => team.Id == teamId && team.CompetitionId == competitionId,
                ct))
            throw new DbUpdateConcurrencyException("The scoring team no longer exists.");
    }

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
            await outbox.PublishAsync(new EvaluateGameplayFact(
                id,
                Guid.CreateVersion7(timeProvider.GetUtcNow())));
    }

    private async Task<BloodAwarded?> TryCreateBloodAwardAsync(
        GameplayFact submission,
        Evaluation evaluation,
        CancellationToken ct)
    {
        if (submission.Kind != GameplayFactKind.FlagAttempt
            || evaluation.Decision.Result != GameplayFactResult.Correct)
            return null;
        if (evaluation.OfficialWindow is not { } officialWindow
            || !officialWindow.Contains(submission.OccurredAt))
            return null;

        var competition = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => new
            {
                competition.Mode,
                competition.TracksEnabled,
                competition.Tracks
            })
            .SingleAsync(ct);
        if (competition.Mode != GameMode.Ctf)
            return null;

        var tracks = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks);
        var currentTrackKey = await db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams)
            .Where(team => team.Id == submission.TeamId)
            .Select(team => team.TrackKey)
            .SingleOrDefaultAsync(ct);
        if (currentTrackKey is null) return null;
        var currentTrack = CtfCompletionEligibility.Track(tracks, currentTrackKey);
        if (currentTrack?.EarnsBlood != true)
            return null;
        var bloodTrackKeys = tracks.Tracks.Where(track => track.EarnsBlood)
            .Select(track => track.Key.ToLowerInvariant())
            .ToArray();

        var solvedTeamIds = await db.GameplayFacts.AsNoTracking()
            .Where(CtfCompletionEligibility.Before(submission.OccurredAt, submission.Id))
            .Where(candidate =>
                candidate.CompetitionId == submission.CompetitionId
                && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                && candidate.Kind == GameplayFactKind.FlagAttempt
                && candidate.Result == GameplayFactResult.Correct
                && candidate.OccurredAt >= officialWindow.StartAt
                && candidate.OccurredAt < officialWindow.EndAt
                && candidate.Id != submission.Id)
            .Join(
                db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams).Where(team => (!competition.TracksEnabled
                        || bloodTrackKeys.Contains(team.TrackKey))
                    && team.RegisteredAt < officialWindow.EndAt),
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

    private sealed record Evaluation(
        GameplayFactDecision Decision,
        CompetitionOfficialWindow? OfficialWindow = null);

    private sealed record GameplayFactProcessingScope(
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        GameplayFactKind Kind,
        Guid? TeamId,
        bool ChallengeWide);

}
