using NoCTF.Application.Common;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public sealed record RequiredCheckEvidence(CheckItemKind Kind, Guid? FlagId = null);
public sealed record CheckProbeEvidence(CheckItemKind Kind, Guid? FlagId, CheckProbeResult Result, CheckFailureReason Reason);
public sealed record CheckEvidenceInput(Guid ExecutionId, Guid RuntimeInstanceId, IReadOnlyList<RequiredCheckEvidence> Required,
    IReadOnlyList<CheckProbeEvidence> Probes, DateTimeOffset StartedAt, DateTimeOffset Deadline, DateTimeOffset ObservedAt, DateTimeOffset ReceivedAt,
    CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact);

public static class CheckEvidenceValidation
{
    public static OperationResult<CheckEvaluation, CapabilityExecutionFailure> Evaluate(CheckEvidenceInput input)
    {
        if (input.ExecutionId == Guid.Empty || !Enum.IsDefined(input.Scope) || input.RuntimeInstanceId == Guid.Empty || input.Required.Count == 0
            || input.Required.Distinct().Count() != input.Required.Count || input.Required.Any(item => !Enum.IsDefined(item.Kind)
                || IsFlag(item.Kind) != item.FlagId.HasValue || item.FlagId == Guid.Empty))
            return Failure(CapabilityExecutionFailure.InvalidInput);
        if (input.ObservedAt < input.StartedAt || input.ObservedAt > input.Deadline || input.ObservedAt > input.ReceivedAt
            || input.ReceivedAt > input.Deadline)
            return Failure(CapabilityExecutionFailure.DeadlineExceeded);
        var expected = input.Required.ToHashSet();
        if (input.Probes.Any(probe => !Enum.IsDefined(probe.Kind) || !Enum.IsDefined(probe.Result) || !Enum.IsDefined(probe.Reason)
                || !expected.Contains(new(probe.Kind, probe.FlagId)) || probe.Result == CheckProbeResult.NotRequired
                || probe.Result == CheckProbeResult.NotExecuted && (!HasFailedDependency(probe, input.Probes)
                    || probe.Reason is not (CheckFailureReason.ConnectionRefused or CheckFailureReason.ConnectTimeout
                        or CheckFailureReason.RequestTimeout or CheckFailureReason.FunctionalCheckFailed))
                || probe.Reason == CheckFailureReason.PlayerMaintenance
                || probe.Result == CheckProbeResult.Passed && probe.Reason != CheckFailureReason.None
                || probe.Result != CheckProbeResult.Passed && probe.Reason == CheckFailureReason.None)
            || input.Probes.Select(probe => (probe.Kind, probe.FlagId)).Distinct().Count() != input.Probes.Count)
            return Failure(CapabilityExecutionFailure.InvalidEvidence);
        if (input.Probes.Count != expected.Count) return Failure(CapabilityExecutionFailure.MissingEvidence);
        var incomplete = input.Probes.Any(probe => probe.Result is CheckProbeResult.PlatformFailure or CheckProbeResult.Unknown);
        return OperationResult<CheckEvaluation, CapabilityExecutionFailure>.Success(new()
        {
            ExecutionId = input.ExecutionId, Scope = input.Scope, RuntimeInstanceId = input.RuntimeInstanceId, ObservedAt = input.ObservedAt,
            ExecutionState = incomplete ? CapabilityExecutionState.PlatformFailed : CapabilityExecutionState.Completed,
            ExecutionFailure = incomplete ? CapabilityExecutionFailure.InvalidEvidence : null,
            Probes = input.Probes.ToArray()
        });
    }

    private static bool IsFlag(CheckItemKind kind) => kind is CheckItemKind.FlagWrite or CheckItemKind.CurrentFlagRead or CheckItemKind.HistoricalFlagRead;
    private static bool HasFailedDependency(CheckProbeEvidence probe, IReadOnlyList<CheckProbeEvidence> evidence) =>
        probe.Kind != CheckItemKind.Connection && evidence.Any(parent => parent.Result == CheckProbeResult.TargetFailure
            && parent.Reason == probe.Reason && (parent.Kind == CheckItemKind.Connection
                || parent.Kind == CheckItemKind.Functionality && probe.Kind != CheckItemKind.Functionality));
    private static OperationResult<CheckEvaluation, CapabilityExecutionFailure> Failure(CapabilityExecutionFailure failure) =>
        OperationResult<CheckEvaluation, CapabilityExecutionFailure>.Failure(failure, "Check evidence does not satisfy the server's observation requirements.");
}
