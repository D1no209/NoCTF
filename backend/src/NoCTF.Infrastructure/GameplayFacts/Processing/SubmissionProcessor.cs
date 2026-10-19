using NoCTF.Infrastructure.Persistence;
using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Challenges;
using System.Globalization;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Application.Observability;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class GameplayFactProcessor(
    NoCtfDbContext db,
    IGameplayFactEvaluatorCatalog evaluatorCatalog,
    IPostCommitMessagePublisher outbox,
    IDbContextFactory<NoCtfDbContext> completionContexts,
    IDatabaseConstraintClassifier constraintClassifier,
    ILogger<GameplayFactProcessor>? logger = null,
    TimeProvider? clock = null) : IGameplayFactProcessor
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly ILogger<GameplayFactProcessor> log =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameplayFactProcessor>.Instance;

    public async Task ProcessAsync(
        Guid gameplayFactId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        var optimistic = await db.GameplayFacts.AsNoTracking()
            .Include(item => item.Awd)
            .Where(item => item.Id == gameplayFactId
                && item.ConcurrencyStamp == expectedConcurrencyStamp)
            .SingleOrDefaultAsync(cancellationToken);
        if (optimistic is null) return;
        await ProcessAsync(optimistic, cancellationToken);
    }

    internal async Task ProcessAsync(GameplayFact optimistic, CancellationToken cancellationToken)
    {
        if (optimistic.State is not (GameplayFactState.Queued or GameplayFactState.Processing)) return;
        NoCtfTelemetry.RecordGameplayFactStage(GameplayFactPerformanceStage.DispatchAge,
            (timeProvider.GetUtcNow() - optimistic.OccurredAt).TotalSeconds);
        var started = Stopwatch.GetTimestamp();
        var directEvaluation = await EvaluateAsync(optimistic, cancellationToken);
        NoCtfTelemetry.RecordGameplayFactStage(GameplayFactPerformanceStage.WorkerEvaluation,
            Stopwatch.GetElapsedTime(started).TotalSeconds);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var completionDb = await completionContexts.CreateDbContextAsync(cancellationToken);
            var writer = new GameplayFactCompletionWriter(completionDb, outbox, timeProvider, log);
            try
            {
                await writer.CompleteAsync(optimistic.Id,
                    directEvaluation with { ExpectedStamp = optimistic.ConcurrencyStamp }, cancellationToken);
                return;
            }
            catch (Exception exception) when (!writer.Committed && (TransactionFailureClassifier.IsRetryable(exception)
                || directEvaluation.Decision.AwdAttack is not null && constraintClassifier.IsAwdCaptureIdentityConflict(exception)))
            {
                NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.CompletionConflict);
                outbox.DiscardPendingMessages();
                // The failed transaction and its tracked graph are disposed before another attempt.
                if (attempt == 2)
                {
                    await using var currentDb = await completionContexts.CreateDbContextAsync(cancellationToken);
                    var current = await currentDb.GameplayFacts.AsNoTracking().Where(item => item.Id == optimistic.Id)
                        .Select(item => new { item.ConcurrencyStamp, item.State }).SingleOrDefaultAsync(cancellationToken);
                    if (current is not null && current.ConcurrencyStamp == optimistic.ConcurrencyStamp
                        && current.State is GameplayFactState.Queued or GameplayFactState.Processing)
                        throw;
                    return;
                }
            }
            catch when (!writer.Committed)
            {
                outbox.DiscardPendingMessages();
                throw;
            }
        }
    }

    private async Task<Evaluation> EvaluateAsync(GameplayFact submission, CancellationToken cancellationToken)
    {
        if (submission.Kind == GameplayFactKind.ManualAdjustment)
            return new(EvaluateManualAdjustment(submission));
        if (submission.Kind == GameplayFactKind.HintUnlock)
            return await EvaluateHintAsync(submission, cancellationToken);
        // Load each configuration root independently. Joining entire aggregates makes
        // every split collection repeat the competition/challenge join tree.
        var competition = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(item => new { item.Id, item.Mode, item.Status, item.StartAt, item.EndAt, item.ModeConfiguration })
            // One configuration leaf owns at most one child collection, so a single query cannot fan out.
            .AsSingleQuery()
            .SingleAsync(cancellationToken);
        var challenge = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == submission.CompetitionChallengeId && item.CompetitionId == competition.Id)
            .Select(item => new { item.Id, item.ChallengeId, item.Rules, item.AutoOpenAt, item.ScoringEndsAt, item.SubmissionDeadlineAt, item.TimingRevision })
            .AsSplitQuery().SingleAsync(cancellationToken);
        var definition = await GameplayChallengeDefinitionReader.ReadAsync(db, challenge.ChallengeId, competition.Mode, cancellationToken);
        var configuration = new
        {
            Competition = competition,
            CompetitionChallenge = challenge,
            Challenge = new { Definition = definition }
        };
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
        var currentTiming = new ChallengeTiming(challenge.AutoOpenAt, challenge.ScoringEndsAt, challenge.SubmissionDeadlineAt);
        if (!practiceFact && submission.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt)
        {
            var rules = new NoCTF.GameModes.Registration.GameModeGameplayFactAdmissionPolicy()
                .GetRules(competition.Mode, competition.ModeConfiguration!, challenge.Rules!, definition);
            if (rules.MaxFlagAttempts is > 0)
            {
                var preceding = db.GameplayFacts.AsNoTracking().Where(item =>
                    item.CompetitionId == submission.CompetitionId && item.TeamId == submission.TeamId
                    && item.CompetitionChallengeId == submission.CompetitionChallengeId && item.Kind == submission.Kind
                    && item.State != GameplayFactState.PlatformFailed
                    && (item.OccurredAt < submission.OccurredAt
                        || item.OccurredAt == submission.OccurredAt && item.Id.CompareTo(submission.Id) < 0)
                    && (challenge.AutoOpenAt == null || item.OccurredAt >= challenge.AutoOpenAt)
                    && (challenge.SubmissionDeadlineAt == null || item.OccurredAt < challenge.SubmissionDeadlineAt));
                if (officialWindow is { } window)
                    preceding = preceding.Where(item => item.OccurredAt >= window.StartAt && item.OccurredAt < window.EndAt);
                if (await preceding.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id)
                    .Skip(rules.MaxFlagAttempts.Value - 1).AnyAsync(cancellationToken))
                    return new(new(GameplayFactResult.AttemptsExhausted, null, submission.OccurredAt),
                        TimingEligibility: currentTiming.Eligibility(submission.OccurredAt), TimingRevision: challenge.TimingRevision);
            }
        }
        if (configuration.Competition.Mode == GameMode.Awd)
        {
            var issued = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId == submission.CompetitionChallengeId
                    && flag.Flag == submission.Value).ToArrayAsync(cancellationToken);
            var rulesIds = issued.Where(flag => flag.AwdIssue != null).Select(flag => flag.AwdIssue!.RulesId).ToArray();
            var issuanceRules = await db.AwdRoundRules.AsNoTracking().AsSplitQuery()
                .Where(snapshot => rulesIds.Contains(snapshot.Id)).ToArrayAsync(cancellationToken);
            return new(evaluatorCatalog.Get(GameMode.Awd).Evaluate(new(submission, [], issued, null,
                configuration.Competition.ModeConfiguration!, configuration.CompetitionChallenge.Rules!,
                AwdIssuanceRules: issuanceRules)), TimingEligibility: submission.TimeEligibility,
                TimingRevision: submission.AppliedTimingRevision);
        }
        var priorQuery = db.GameplayFacts.AsNoTracking()
            .Where(item =>
                item.CompetitionId == submission.CompetitionId
                && item.CompetitionChallengeId == submission.CompetitionChallengeId
                && item.TeamId == submission.TeamId
                && ((item.Result == GameplayFactResult.Correct || item.Result == GameplayFactResult.RightButDue)
                    && (currentTiming.AutoOpenAt == null || item.OccurredAt >= currentTiming.AutoOpenAt)
                    && (practiceFact || currentTiming.SubmissionDeadlineAt == null || item.OccurredAt < currentTiming.SubmissionDeadlineAt)
                    || item.FailureCode != null && CheatIncidentFailures.All.Contains(item.FailureCode.Value))
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
        foreach (var prior in priorSubmissions)
        {
            prior.TimeEligibility = currentTiming.Eligibility(prior.OccurredAt, practiceFact);
            if (prior.Result is { } result) prior.Result = currentTiming.Classify(result, prior.OccurredAt, practiceFact);
        }
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
        var decision = evaluatorCatalog.Get(configuration.Competition.Mode).Evaluate(new(
            submission,
            priorSubmissions,
            flags,
            patch,
            configuration.Competition.ModeConfiguration!,
            configuration.CompetitionChallenge.Rules!,
            configuration.Competition.StartAt,
            null,
            configuration.Challenge.Definition));
        if (submission.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt)
            return new(decision with { Result = decision.Result is { } result
                ? currentTiming.Classify(result, submission.OccurredAt, practiceFact) : null },
                TimingEligibility: currentTiming.Eligibility(submission.OccurredAt, practiceFact), TimingRevision: challenge.TimingRevision);
        return new(decision);
    }

    private async Task<Evaluation> EvaluateHintAsync(GameplayFact submission, CancellationToken ct)
    {
        if (submission.ReferenceKind != GameplayFactReferenceKind.Hint
            || submission.ReferenceId is not Guid hintId)
            return new(new(GameplayFactResult.Rejected, GameplayFactFailureCode.HintUnavailable, submission.OccurredAt));
        var cost = await db.CompetitionChallenges.AsNoTracking().Where(item => item.Id == submission.CompetitionChallengeId
                && item.CompetitionId == submission.CompetitionId && item.IsPublished
                && db.Competitions.Any(competition => competition.Id == item.CompetitionId && competition.Status == CompetitionStatus.Running))
            .SelectMany(item => item.Hints.Where(hint => hint.Id == hintId && hint.PublishedAt != null
                && hint.PublishedAt <= submission.OccurredAt && hint.HiddenAt == null).Select(hint => (long?)hint.Cost))
            .SingleOrDefaultAsync(ct);
        return cost is { } value
            ? new(new(GameplayFactResult.Unlocked, null, submission.OccurredAt,
                ReferenceKind: GameplayFactReferenceKind.Hint, ReferenceId: hintId), HintCost: value)
            : new(new(GameplayFactResult.Rejected, GameplayFactFailureCode.HintUnavailable, submission.OccurredAt));
    }

    private static GameplayFactDecision EvaluateManualAdjustment(GameplayFact submission)
    {
        var valid = int.TryParse(submission.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
            && delta != 0 && delta.ToString(CultureInfo.InvariantCulture) == submission.Value;
        return valid
            ? new(GameplayFactResult.Applied, null, submission.OccurredAt)
            : new(null, GameplayFactFailureCode.HintUnavailable, submission.OccurredAt);
    }
}

internal sealed record Evaluation(
    GameplayFactDecision Decision,
    Guid ExpectedStamp = default,
    GameplayFactTimeEligibility TimingEligibility = GameplayFactTimeEligibility.Valid,
    Guid TimingRevision = default,
    long? HintCost = null);

internal sealed class GameplayFactCompletionWriter(NoCtfDbContext db, IPostCommitMessagePublisher outbox,
    TimeProvider timeProvider, ILogger<GameplayFactProcessor> log)
{
    private readonly ICompetitionEventRecorder events = new CompetitionEventStore(db, outbox);
    public bool Committed { get; private set; }
    private static readonly CompetitionEventKind[] CheatResolutionKinds =
    [
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected
    ];

    public async Task CompleteAsync(
        Guid gameplayFactId,
        Evaluation evaluation,
        CancellationToken cancellationToken)
    {
        var completionStarted = Stopwatch.GetTimestamp();
        // Hint unlocks serialize their duplicate and balance reads. Other completions rely on the fact's
        // concurrency stamp and unique constraints, so their transaction only spans the final write.
        await using var transaction = new LateTransaction(db);
        if (evaluation.HintCost is not null)
            await transaction.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var submission = await db.GameplayFacts.Include(fact => fact.Awd).SingleOrDefaultAsync(
            item => item.Id == gameplayFactId, cancellationToken);
        if (submission is null || submission.ConcurrencyStamp != evaluation.ExpectedStamp
            || submission.State is not (GameplayFactState.Queued or GameplayFactState.Processing))
            return;
        db.Entry(submission).Property(item => item.ConcurrencyStamp).OriginalValue = evaluation.ExpectedStamp;
        var now = timeProvider.GetUtcNow();
        var firstAdjudication = submission.Result is null
            && submission.FailureCode is null;
        if (submission.Kind is GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment)
            await EnsureScoringTeamExistsAsync(submission.CompetitionId, submission.TeamId!.Value, cancellationToken);

        if (evaluation.HintCost is { } cost)
        {
            var duplicate = await db.GameplayFacts.AsNoTracking().AnyAsync(item => item.Id != submission.Id
                && item.CompetitionId == submission.CompetitionId && item.TeamId == submission.TeamId
                && item.Kind == GameplayFactKind.HintUnlock && item.ReferenceKind == GameplayFactReferenceKind.Hint
                && item.ReferenceId == submission.ReferenceId && item.State == GameplayFactState.Completed
                && item.Result == GameplayFactResult.Unlocked, cancellationToken);
            if (duplicate)
                evaluation = evaluation with { Decision = evaluation.Decision with { Result = GameplayFactResult.Duplicate } };
            else if (cost > 0 && await new HintBalanceReader(db).ReadAsync(submission, now, cancellationToken) < cost)
                evaluation = evaluation with { Decision = evaluation.Decision with { Result = GameplayFactResult.Rejected,
                    FailureCode = GameplayFactFailureCode.InsufficientScore } };
        }

        if (evaluation.Decision.AwdAttack is not null)
            evaluation = evaluation with { Decision = await ApplyAwdAttackAsync(submission, evaluation.Decision, cancellationToken) };

        if (evaluation.Decision.Result is null)
        {
            submission.State = GameplayFactState.PlatformFailed;
            submission.FailureCode =
                evaluation.Decision.FailureCode ?? GameplayFactFailureCode.CheckerPlatformError;
            submission.UpdatedAt = now;
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.GameplayFactAdjudicated,
                CompetitionEventLevel.Error,
                CompetitionEventVisibility.Team,
                now,
                ActorUserId: submission.ActorUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId!.Value,
                GameplayFactId: submission.Id,
                GameplayFactKind: submission.Kind,
                GameplayFactState: submission.State,
                GameplayFactResult: submission.Result), cancellationToken);
            await RecordAwdpBreakResolutionAsync(submission, now, cancellationToken);
            await transaction.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Committed = true;
            NoCtfTelemetry.RecordGameplayFactStage(GameplayFactPerformanceStage.WorkerCompletion,
                Stopwatch.GetElapsedTime(completionStarted).TotalSeconds);
            NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.Completed);
            await transaction.DisposeAsync();
            await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State, submission.ConcurrencyStamp));
            if (firstAdjudication)
                RecordProcessingTelemetry(submission, now);
            await outbox.FlushCommittedMessagesAsync();
            return;
        }

        var previousFailureCode = submission.FailureCode;
        if (CheatIncidentFailures.IsIncident(submission.FailureCode))
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
                    CompetitionChallengeId: submission.CompetitionChallengeId!.Value,
                    GameplayFactId: submission.Id,
                    GameplayFactKind: submission.Kind,
                    GameplayFactResult: submission.Result,
                    Reason: "Superseded by gameplay fact re-evaluation."), cancellationToken);
            }
        }
        submission.VictimTeamId = evaluation.Decision.VictimTeamId;
        submission.TimeEligibility = evaluation.TimingEligibility;
        submission.AppliedTimingRevision = evaluation.TimingRevision;
        submission.Result = evaluation.Decision.Result;
        submission.FailureCode = evaluation.Decision.FailureCode;
        submission.ReferenceKind = evaluation.Decision.ReferenceKind ?? submission.ReferenceKind;
        submission.ReferenceId = evaluation.Decision.ReferenceId ?? submission.ReferenceId;
        submission.State = GameplayFactState.Completed;
        submission.UpdatedAt = now;
        if (CheatIncidentFailures.IsIncident(submission.FailureCode)
            && submission.TeamId is Guid sourceTeamId
            && submission.ActorUserId is Guid actorUserId)
        {
            if (submission.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected
                && submission.VictimTeamId is Guid ownerTeamId)
            {
                await outbox.PublishAsync(new ForeignTeamFlagDetected(
                    submission.CompetitionId,
                    submission.Id,
                    sourceTeamId,
                    ownerTeamId,
                    actorUserId,
                    submission.CompetitionChallengeId!.Value,
                    submission.OccurredAt));
            }
            if (submission.FailureCode != GameplayFactFailureCode.ForeignTeamFlagDetected)
                await outbox.PublishAsync(new StaticFlagAcquisitionViolationDetected(
                    submission.CompetitionId, submission.Id, sourceTeamId, actorUserId,
                    submission.CompetitionChallengeId!.Value, submission.FailureCode!.Value, submission.OccurredAt));
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.CheatIncidentDetected,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                submission.OccurredAt,
                ActorUserId: submission.ActorUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId!.Value,
                GameplayFactId: submission.Id,
                GameplayFactKind: submission.Kind,
                GameplayFactResult: submission.Result), cancellationToken);
            log.LogWarning(
                "Cheat incident detected for competition {CompetitionId}, gameplay fact {GameplayFactId}, source team {SourceTeamId}, owner team {OwnerTeamId}.",
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
            CompetitionChallengeId: submission.CompetitionChallengeId!.Value,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            GameplayFactState: submission.State,
            GameplayFactResult: submission.Result), cancellationToken);
        await RecordAwdpBreakResolutionAsync(submission, now, cancellationToken);
        await transaction.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Committed = true;
        NoCtfTelemetry.RecordGameplayFactStage(GameplayFactPerformanceStage.WorkerCompletion,
            Stopwatch.GetElapsedTime(completionStarted).TotalSeconds);
        NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.Completed);
        await transaction.DisposeAsync();
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State, submission.ConcurrencyStamp));
        if (submission.Kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt
            && GameplayFactCompletion.IsSuccessful(submission.Result, submission.TimeEligibility))
            await outbox.PublishAsync(new StopSolvedChallengeRuntimes(submission.Id, submission.ConcurrencyStamp));
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

    private async Task<GameplayFactDecision> ApplyAwdAttackAsync(GameplayFact fact,
        GameplayFactDecision decision, CancellationToken ct)
    {
        var attack = decision.AwdAttack!;
        var details = fact.Awd ??= new AwdFactDetails { GameplayFactId = fact.Id };
        if (attack.FlagId is { } flagId && attack.Outcome == AwdAttackOutcome.Accepted)
        {
            var issue = await db.AwdFlagIssues.AsNoTracking().SingleAsync(item => item.FlagId == flagId, ct);
            if (issue.CompetitionId != fact.CompetitionId || issue.CompetitionChallengeId != fact.CompetitionChallengeId
                || issue.TeamId != decision.VictimTeamId || issue.RoundId != attack.GenerationRoundId
                || issue.RulesId != attack.RulesId || fact.TeamId is null || fact.TeamId == issue.TeamId)
                throw new InvalidOperationException("AWD capture scope does not match its issued Flag.");
            if (issue.State != AwdFlagIssueState.Issued)
            {
                attack = attack with { Outcome = AwdAttackOutcome.NotScorable };
                decision = decision with { Result = GameplayFactResult.Rejected,
                    FailureCode = issue.State == AwdFlagIssueState.Revoked
                        ? GameplayFactFailureCode.FlagRevoked : GameplayFactFailureCode.FlagNotIssued };
            }
            else
            {
                var capture = await db.AwdCaptureIdentities.SingleOrDefaultAsync(item =>
                    item.FlagId == flagId && item.AttackingTeamId == fact.TeamId, ct);
                if (capture is not null && capture.GameplayFactId != fact.Id)
                {
                    attack = attack with { Outcome = AwdAttackOutcome.Duplicate };
                    decision = decision with { Result = GameplayFactResult.Duplicate, FailureCode = GameplayFactFailureCode.DuplicateAttack };
                }
                else if (capture is null)
                    db.AwdCaptureIdentities.Add(new AwdCaptureIdentity
                    {
                        CompetitionId = fact.CompetitionId, FlagId = flagId,
                        AttackingTeamId = fact.TeamId.Value, GameplayFactId = fact.Id
                    });
            }
        }
        if (attack.Outcome != AwdAttackOutcome.Accepted)
        {
            var previous = await db.AwdCaptureIdentities.SingleOrDefaultAsync(item => item.GameplayFactId == fact.Id, ct);
            if (previous is not null) db.AwdCaptureIdentities.Remove(previous);
        }
        details.AttackOutcome = attack.Outcome;
        details.FlagId = attack.FlagId;
        details.GenerationRoundId = attack.GenerationRoundId;
        details.RulesId = attack.RulesId;
        details.Settlement = attack.Outcome == AwdAttackOutcome.SystemError
            ? AwdSettlementState.PendingAdjudication : AwdSettlementState.Final;
        if (attack.Outcome == AwdAttackOutcome.Accepted && attack.RulesId is { } rulesId)
        {
            var settlement = await db.AwdRoundRules.AsNoTracking().Where(rules => rules.Id == rulesId)
                .Select(rules => rules.Options.AttackSettlement).SingleAsync(ct);
            details.Settlement = settlement == AwdAttackSettlement.Immediate
                ? AwdSettlementState.Final : AwdSettlementState.Provisional;
        }
        return decision with { AwdAttack = attack };
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
            CompetitionChallengeId: submission.CompetitionChallengeId!.Value,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            GameplayFactState: submission.State,
            GameplayFactResult: submission.Result), ct);
    }

    private async Task EnsureScoringTeamExistsAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        if (!await db.Teams.AsNoTracking().AnyAsync(
                team => team.Id == teamId && team.CompetitionId == competitionId,
                ct))
            throw new DbUpdateConcurrencyException("The scoring team no longer exists.");
    }

}
