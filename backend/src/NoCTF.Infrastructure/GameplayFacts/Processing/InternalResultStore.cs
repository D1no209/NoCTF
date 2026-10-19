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
using NoCTF.GameModes.PatchVerification.Scoring;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    ProgressionReconciler? progressionReconciler = null, NoCTF.Infrastructure.Authentication.PlatformSecretProtector? secrets = null) : IInternalResultStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken ct) =>
        await RecordPatchVerificationCoreAsync(result, ct);

    public async Task<InternalResultDisposition> RecordPatchVerificationAsync(
        AwdpFixResult result,
        CancellationToken ct) =>
        await RecordPatchVerificationCoreAsync(result, ct);

    private async Task<InternalResultDisposition> RecordPatchVerificationCoreAsync(
        AwdpFixResult result,
        CancellationToken ct)
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
            || runtime.Purpose is not (RuntimePurpose.AwdpTarget
                or RuntimePurpose.PatchVerificationTarget)
            || runtime.GameplayFactId != fact.Id)
            return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
            return InternalResultDisposition.Duplicate;
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
                    IsCtfPatchVerification = template.Definition is CtfChallengeDefinition
                        && ((CtfChallengeDefinition)template.Definition).InteractionKind
                            == CtfInteractionKind.PatchVerification
                })
            .SingleAsync(ct);
        var validMode = context.Mode == GameMode.Awdp
            && runtime.Purpose == RuntimePurpose.AwdpTarget
            || context.Mode == GameMode.Ctf
            && runtime.Purpose == RuntimePurpose.PatchVerificationTarget
            && context.IsCtfPatchVerification;
        if (!validMode
            || fact.Kind != GameplayFactKind.FixAttempt
            || fact.ReferenceKind != GameplayFactReferenceKind.PatchUpload
            || fact.ReferenceId is null
            || fact.TeamId is null)
            return InternalResultDisposition.NotFound;
        var resolvedAt = ToPostgresPrecision(result.OccurredAt);
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
        var decision = context.Mode == GameMode.Ctf
            ? CtfPatchVerificationOutcomeMapper.Map(result.Outcome)
            : MapAwdpOutcome(result.Outcome);
        var timing = new ChallengeTiming(context.AutoOpenAt, context.ScoringEndsAt, context.SubmissionDeadlineAt);
        fact.TimeEligibility = timing.Eligibility(fact.OccurredAt);
        fact.AppliedTimingRevision = context.TimingRevision;
        decision = decision with { Result = timing.Classify(decision.Result, fact.OccurredAt) };
        fact.Result = decision.Result;
        fact.State = GameplayFactState.Completed;
        fact.FailureCode = decision.FailureCode;
        fact.UpdatedAt = resolvedAt;
        runtime.State = RuntimeState.Stopping;
        if (context.Mode == GameMode.Ctf
            && NoCTF.Domain.Challenges.GameplayFactCompletion.IsSuccessful(decision.Result, fact.TimeEligibility))
        {
            var playerRuntimeIds = await db.RuntimeInstances
                .Where(instance => instance.CompetitionId == fact.CompetitionId
                    && instance.CompetitionChallengeId == fact.CompetitionChallengeId
                    && instance.TeamId == fact.TeamId
                    && (instance.Purpose == RuntimePurpose.Player
                        || instance.Purpose == RuntimePurpose.Practice)
                    && (instance.State == RuntimeState.Queued
                        || instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping))
                .Select(instance => instance.Id)
                .ToArrayAsync(ct);
            foreach (var playerRuntimeId in playerRuntimeIds)
                await outbox.PublishAsync(new StopRuntime(playerRuntimeId));
        }
        await events.RecordAsync(new(
            fact.CompetitionId,
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
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            context.Mode == GameMode.Ctf
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Team,
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
                fact.CompetitionId,
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
        await events.RecordAsync(new(
            runtime.CompetitionId!.Value,
            CompetitionEventKind.RuntimeStateChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            resolvedAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            RuntimeState: RuntimeState.Stopping), ct);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner."),
            resolvedAt));
        if (context.Mode == GameMode.Ctf)
            await (progressionReconciler ?? new ProgressionReconciler(db))
                .ReconcileCompletedFactAsync(fact, resolvedAt, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
        await outbox.FlushCommittedMessagesAsync();
        return InternalResultDisposition.Applied;
    }

    private static DateTimeOffset ToPostgresPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static PatchVerificationDecision MapAwdpOutcome(AwdpFixOutcome outcome)
    {
        var decision = AwdpFixOutcomeMapper.Map(outcome);
        return new(decision.Result, decision.FailureCode);
    }

}
