using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore
{
    public async Task<InternalResultDisposition> CompleteFixWorkAsync(CompleteFixExecutionWork completion, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == completion.GameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, FixExecution: { TargetKind: FixTargetKind.Isolated } execution }
            || execution.RuntimeInstanceId != completion.RuntimeInstanceId) return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed) return InternalResultDisposition.Duplicate;
        if (execution.WorkCompletedAt is not null && completion.Result.Kind != FixWorkCompletionKind.PlatformFailed)
            return InternalResultDisposition.Duplicate;
        var outcome = FixWorkOutcome(fact, completion.Result);
        if (outcome == AwdpFixOutcome.PlatformFailed && completion.Result.Kind != FixWorkCompletionKind.PlatformFailed)
            completion = completion with { Result = new(FixWorkCompletionKind.PlatformFailed,
                CapabilityExecutionFailure.MissingEvidence, execution.Phase) };
        return await RecordFixCoreAsync(new(completion.GameplayFactId, completion.RuntimeInstanceId, outcome, completion.OccurredAt),
            null, ct, completion.Result);
    }

    public async Task<InternalResultDisposition> FinalizeFixExecutionAsync(Guid gameplayFactId, Guid runtimeInstanceId,
        DateTimeOffset now, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == gameplayFactId, ct);
        if (fact is not { Kind: GameplayFactKind.FixAttempt, FixExecution: { TargetKind: FixTargetKind.Isolated } execution }
            || execution.RuntimeInstanceId != runtimeInstanceId) return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed) return InternalResultDisposition.Duplicate;
        if (execution.Phase != FixExecutionPhase.Finalizing || execution.WorkCompletedAt is null) return InternalResultDisposition.Superseded;
        return await RecordFixCoreAsync(new(gameplayFactId, runtimeInstanceId, FixWorkOutcome(fact, null), now), null, ct, finalize: true);
    }

    private static AwdpFixOutcome FixWorkOutcome(GameplayFact fact, FixWorkCompletion? completion)
    {
        if (completion?.Kind == FixWorkCompletionKind.PlatformFailed || fact.FixExecution?.ExecutionFailure is not null)
            return AwdpFixOutcome.PlatformFailed;
        var fix = fact.CapabilityOutcomes.OfType<FixOutcome>().SingleOrDefault();
        if (completion?.Kind == FixWorkCompletionKind.Rejected && fix is null) return AwdpFixOutcome.PatchFailed;
        if (fix?.ExecutionState != CapabilityExecutionState.Completed) return AwdpFixOutcome.PlatformFailed;
        if (fix.Result == FixResult.TimedOut) return AwdpFixOutcome.PatchTimeout;
        if (fix.Result == FixResult.Failed) return AwdpFixOutcome.PatchFailed;
        if (fix.Result != FixResult.Applied) return AwdpFixOutcome.PlatformFailed;
        var check = fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault();
        if (check?.ExecutionState != CapabilityExecutionState.Completed || !check.Probes.Any(probe => probe.Kind == CheckItemKind.Functionality))
            return AwdpFixOutcome.PlatformFailed;
        if (check.Probes.Any(probe => probe.Kind is CheckItemKind.Connection or CheckItemKind.Functionality && probe.Result != CheckProbeResult.Passed))
            return AwdpFixOutcome.ServiceAbnormal;
        return check.Probes.Any(probe => probe.Kind == CheckItemKind.Vulnerability && probe.Result != CheckProbeResult.Passed)
            ? AwdpFixOutcome.ExploitSucceeded : AwdpFixOutcome.DefenseSucceeded;
    }

    private async Task QueueFixTargetStopAsync(RuntimeInstance runtime, DateTimeOffset at, CancellationToken ct)
    {
        if (runtime.State == RuntimeState.Stopped) return;
        if (runtime.State != RuntimeState.Stopping)
        {
            runtime.State = RuntimeState.Stopping;
            await events.RecordAsync(new(runtime.CompetitionId!.Value, CompetitionEventKind.RuntimeStateChanged,
                CompetitionEventLevel.Information, CompetitionEventVisibility.Team, at, TeamId: runtime.TeamId,
                CompetitionChallengeId: runtime.CompetitionChallengeId, RuntimeInstanceId: runtime.Id,
                GameplayFactId: runtime.GameplayFactId, RuntimeState: RuntimeState.Stopping), ct);
        }
        await outbox.PublishAsync(new StopRuntime(runtime.Id));
    }

    private static bool SameFixCheckEvidence(CheckOutcome? previous, FixCheckResult check) => previous is not null
        && previous.ObservedAt == check.ObservedAt && previous.Probes.Count == check.Probes.Count
        && previous.Probes.All(probe => check.Probes.Any(candidate => candidate.Kind == probe.Kind && candidate.FlagId == probe.FlagId
            && candidate.Result == probe.Result && candidate.Reason == probe.Reason));
}
