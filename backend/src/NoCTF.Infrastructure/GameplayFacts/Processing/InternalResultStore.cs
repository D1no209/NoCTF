using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Infrastructure.Authentication.PlatformSecretProtector? secrets = null) : IInternalResultStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken ct) => await RecordFixCoreAsync(result, null, ct);

    public async Task<InternalResultDisposition> RecordFixCheckAsync(FixCheckResult result, CancellationToken ct) =>
        await RecordFixCoreAsync(new(result.GameplayFactId, result.RuntimeInstanceId, AwdpFixOutcome.PlatformFailed, result.ReceivedAt), result, ct);

    private async Task<InternalResultDisposition> RecordFixCoreAsync(AwdpFixResult result, FixCheckResult? check,
        CancellationToken ct, NoCTF.Application.Challenges.Capabilities.FixWorkCompletion? completion = null, bool finalize = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == result.GameplayFactId,
            ct);
        if (fact is null)
            return InternalResultDisposition.NotFound;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null
            || runtime.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
            || runtime.GameplayFactId != fact.Id)
            return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
        {
            if (check is null) return InternalResultDisposition.Duplicate;
            var previous = fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault();
            return SameFixCheckEvidence(previous, check)
                ? InternalResultDisposition.Duplicate : InternalResultDisposition.Conflict;
        }
        if (finalize)
        {
            if (fact.FixExecution is not { Phase: FixExecutionPhase.Finalizing, WorkCompletedAt: not null })
                return InternalResultDisposition.Superseded;
            if (runtime.State == RuntimeState.Failed)
            {
                result = result with { Outcome = AwdpFixOutcome.PlatformFailed };
                completion = new(NoCTF.Application.Challenges.Capabilities.FixWorkCompletionKind.PlatformFailed,
                    CapabilityExecutionFailure.ResourceCleanupFailed, FixExecutionPhase.Finalizing);
            }
            else if (runtime.State != RuntimeState.Stopped || runtime.CapacityAllocations.Items.Count != 0)
            {
                await QueueFixTargetStopAsync(runtime, result.OccurredAt, ct);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await outbox.FlushCommittedMessagesAsync();
                return InternalResultDisposition.Superseded;
            }
        }
        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == fact.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges,
                scope => scope.Challenge.ChallengeId,
                challenge => challenge.Id,
                (scope, template) => new
                {
                    scope.Competition.Mode,
                    scope.Competition.FrozenStartAt,
                    scope.Competition.HiddenStartAt,
                    scope.Challenge.AutoOpenAt,
                    scope.Challenge.ScoringEndsAt,
                    scope.Challenge.SubmissionDeadlineAt,
                    scope.Challenge.TimingRevision,
                    LiveSoloRoundId = db.LiveSoloSubmissions.Where(submission => submission.GameplayFactId == fact.Id
                        && db.Set<NoCTF.Domain.LiveSolo.LiveSoloFixTargetOwnership>().Any(owner => owner.RuntimeInstanceId == runtime.Id
                            && owner.RoundQuestionId == submission.RoundQuestionId)
                        && db.LiveSoloRounds.Any(round => round.Id == submission.RoundId
                            && (round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.Running || round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.ConfirmingResult)
                            && db.LiveSoloMatches.Any(match => match.Id == round.MatchId && match.CurrentRoundId == round.Id
                                && match.PendingCorrectionId == null && match.SupersededAt == null)))
                        .Select(submission => (Guid?)submission.RoundId).SingleOrDefault(),
                    SupportsFix = template.Definition!.Capabilities.Any(capability => capability.Kind == NoCTF.Domain.Challenges.Capabilities.ChallengeCapabilityKind.Fix)
                })
            .SingleAsync(ct);
        var validMode = context.Mode == GameMode.Awdp
            && runtime.Purpose == RuntimePurpose.FixTarget
            || context.Mode == GameMode.LiveSolo
            && runtime.Purpose == RuntimePurpose.ScopedFixTarget
            && context.SupportsFix && context.LiveSoloRoundId != null;
        if (!validMode
            || fact.Kind != GameplayFactKind.FixAttempt
            || fact.ReferenceKind != GameplayFactReferenceKind.PatchUpload
            || fact.ReferenceId is null
            || fact.TeamId is null)
            return InternalResultDisposition.NotFound;
        if (check is not null)
        {
            if (fact.FixExecution?.Phase == FixExecutionPhase.Finalizing)
                return SameFixCheckEvidence(fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault(), check)
                    ? InternalResultDisposition.Duplicate : InternalResultDisposition.Conflict;
            if (fact.FixExecution is not { Phase: FixExecutionPhase.Checking, AppliedAt: { } appliedAt } execution
                || execution.RuntimeInstanceId != result.RuntimeInstanceId
                || fact.CapabilityOutcomes.OfType<FixOutcome>().SingleOrDefault() is not { ExecutionState: CapabilityExecutionState.Completed, Result: FixResult.Applied })
                return InternalResultDisposition.Superseded;
            var definition = await db.Set<CheckCapabilityDefinition>().AsNoTracking()
                .SingleAsync(capability => db.CompetitionChallenges.Any(challenge => challenge.Id == fact.CompetitionChallengeId
                    && challenge.ChallengeId == capability.ChallengeId), ct);
            var required = definition.Requirements.Where(item => item.Kind is CheckItemKind.Connection or CheckItemKind.Functionality or CheckItemKind.Vulnerability)
                .Select(item => new RequiredCheckEvidence(item.Kind)).Append(new(CheckItemKind.Functionality)).Distinct().ToArray();
            var evaluated = CheckEvidenceValidation.Evaluate(new(fact.Id, runtime.Id, required, check.Probes,
                appliedAt, execution.Deadline, check.ObservedAt, check.ReceivedAt));
            if (!evaluated.Succeeded)
            {
                if (evaluated.FailureCode == CapabilityExecutionFailure.DeadlineExceeded) return InternalResultDisposition.Superseded;
                fact.CapabilityOutcomes.Add(new CheckOutcome
                {
                    GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, ObservedAt = check.ReceivedAt,
                    ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = evaluated.FailureCode
                });
            }
            else
            {
                var evidence = evaluated.Value!;
                fact.CapabilityOutcomes.Add(evidence.ToGameplayOutcome());
                result = result with { Outcome = evidence.ExecutionState == CapabilityExecutionState.PlatformFailed ? AwdpFixOutcome.PlatformFailed
                    : evidence.Probes.Any(probe => probe.Kind is CheckItemKind.Connection or CheckItemKind.Functionality && probe.Result != CheckProbeResult.Passed)
                        ? AwdpFixOutcome.ServiceAbnormal
                    : evidence.Probes.Any(probe => probe.Kind == CheckItemKind.Vulnerability && probe.Result != CheckProbeResult.Passed)
                        ? AwdpFixOutcome.ExploitSucceeded : AwdpFixOutcome.DefenseSucceeded };
            }
        }
        else if (completion is null && !finalize
            && result.Outcome is AwdpFixOutcome.DefenseSucceeded or AwdpFixOutcome.ExploitSucceeded or AwdpFixOutcome.ServiceAbnormal)
            return InternalResultDisposition.Conflict;
        if (completion is not null && fact.FixExecution is { } work)
        {
            if (completion.Kind == NoCTF.Application.Challenges.Capabilities.FixWorkCompletionKind.Rejected
                && !fact.CapabilityOutcomes.Any(outcome => outcome.Kind == ChallengeCapabilityKind.Fix))
                fact.CapabilityOutcomes.Add(new FixOutcome { GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id,
                    ObservedAt = result.OccurredAt, ExecutionState = CapabilityExecutionState.Completed, Result = FixResult.Failed });
            if (!finalize && work.WorkCompletedAt is null && result.OccurredAt > work.Deadline)
            {
                completion = new(NoCTF.Application.Challenges.Capabilities.FixWorkCompletionKind.PlatformFailed,
                    CapabilityExecutionFailure.DeadlineExceeded, work.Phase);
                result = result with { Outcome = AwdpFixOutcome.PlatformFailed };
            }
            if (completion.Kind == NoCTF.Application.Challenges.Capabilities.FixWorkCompletionKind.PlatformFailed)
            {
                work.FailedPhase ??= completion.FailedPhase ?? work.Phase;
                work.ExecutionFailure ??= completion.Failure ?? CapabilityExecutionFailure.ExecutionInterrupted;
                if (fact.CapabilityOutcomes.OfType<FixOutcome>().Any(outcome => outcome.Result == FixResult.Applied))
                {
                    var evidence = fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault();
                    if (evidence is null) { evidence = new() { GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, ObservedAt = result.OccurredAt }; fact.CapabilityOutcomes.Add(evidence); }
                    evidence.ExecutionState = CapabilityExecutionState.PlatformFailed; evidence.ExecutionFailure = work.ExecutionFailure;
                }
            }
        }
        var resolvedAt = fact.FixExecution?.JudgedAt ?? result.OccurredAt;
        if (result.Outcome == AwdpFixOutcome.PlatformFailed)
        {
            var convergence = await AwdpFixFailureConvergence
                .ConvergeAwdpFixFailureAsync(
                    runtime,
                    db,
                    outbox,
                    events,
                    resolvedAt,
                    AwdpFixRuntimeCleanupMode.EnsureStop,
                    ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return convergence.FactConverged
                ? InternalResultDisposition.Applied
                : InternalResultDisposition.Duplicate;
        }
        NoCTF.Application.GameplayFacts.Processing.GameplayFactDecision decision;
        if (context.Mode == GameMode.LiveSolo) decision = LiveSoloFixDecision(result.Outcome, resolvedAt);
        else
        {
            var awdpDecision = AwdpFixOutcomeMapper.Map(result.Outcome);
            decision = new(awdpDecision.Result, awdpDecision.FailureCode, resolvedAt);
        }
        var timing = new ChallengeTiming(context.AutoOpenAt, context.ScoringEndsAt, context.SubmissionDeadlineAt);
        fact.TimeEligibility = timing.Eligibility(fact.OccurredAt);
        fact.AppliedTimingRevision = context.TimingRevision;
        decision = decision with { Result = timing.Classify(decision.Result ?? throw new InvalidOperationException("Fix completion requires a current judgement."), fact.OccurredAt) };
        fact.Result = decision.Result;
        fact.State = finalize ? GameplayFactState.Completed : GameplayFactState.Processing;
        fact.FailureCode = decision.FailureCode;
        fact.UpdatedAt = resolvedAt;
        if (fact.FixExecution is { } details)
        {
            details.JudgedAt ??= resolvedAt;
            details.Phase = finalize ? FixExecutionPhase.Finished : FixExecutionPhase.Finalizing;
            if (completion is not null) details.WorkCompletedAt ??= result.OccurredAt;
            if (finalize) details.FinalizedAt = result.OccurredAt;
        }
        if (!finalize)
        {
            if (completion is not null) await QueueFixTargetStopAsync(runtime, result.OccurredAt, ct);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
            await outbox.FlushCommittedMessagesAsync();
            return InternalResultDisposition.Applied;
        }
        await events.RecordAsync(new(
            fact.CompetitionId!.Value,
            CompetitionEventKind.ScoringRecorded,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            resolvedAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId!.Value,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result), ct);
        var adjudicationEventId = await events.RecordAsync(new(
            fact.CompetitionId!.Value,
            CompetitionEventKind.GameplayFactAdjudicated,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            resolvedAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId!.Value,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result,
            RuntimeState: runtime.State), ct);
        if (context.Mode == GameMode.Awdp)
        {
            await events.RecordAsync(new(
                fact.CompetitionId!.Value,
                CompetitionEventKind.AwdpFixResolved,
                result.Outcome == AwdpFixOutcome.PlatformFailed
                    ? CompetitionEventLevel.Error
                    : CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                resolvedAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId!.Value,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactState: fact.State,
                GameplayFactResult: fact.Result,
                PatchUploadId: fact.ReferenceId.Value,
                AwdpFixOutcome: result.Outcome,
                GameplayFactFailureCode: fact.FailureCode,
                ResolvedAt: resolvedAt), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        if (context.LiveSoloRoundId is Guid roundId) await outbox.PublishAsync(new NoCTF.Application.LiveSolo.Rounds.ResolveLiveSoloSubmissionResults(roundId));
        await outbox.FlushCommittedMessagesAsync();
        return InternalResultDisposition.Applied;
    }

    private static NoCTF.Application.GameplayFacts.Processing.GameplayFactDecision LiveSoloFixDecision(AwdpFixOutcome outcome, DateTimeOffset at) => outcome switch
    {
        AwdpFixOutcome.DefenseSucceeded => new(GameplayFactResult.Correct, null, at),
        AwdpFixOutcome.ExploitSucceeded => new(GameplayFactResult.Wrong, GameplayFactFailureCode.FixStillExploitable, at),
        AwdpFixOutcome.ServiceAbnormal => new(GameplayFactResult.Rejected, GameplayFactFailureCode.FixServiceAbnormal, at),
        AwdpFixOutcome.PatchTimeout => new(GameplayFactResult.Rejected, GameplayFactFailureCode.FixExecutionTimedOut, at),
        AwdpFixOutcome.PatchFailed => new(GameplayFactResult.Rejected, GameplayFactFailureCode.FixExecutionFailed, at),
        AwdpFixOutcome.PlatformFailed => new(GameplayFactResult.Rejected, GameplayFactFailureCode.FixPlatformFailure, at),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

}
