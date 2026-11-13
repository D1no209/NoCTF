using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;
using NoCTF.Application.GameplayFacts.Processing;

namespace NoCTF.Application.Challenges.Capabilities;

public interface IControlResultStore
{
    Task<InternalResultDisposition> RecordAsync(Guid executionId, Guid runtimeId, CapabilityExecutionScope scope,
        string runnerId, DateTimeOffset deadline, ControlEvidence evidence, CancellationToken ct);
}

public sealed record ControlExecutionInput(Guid ExecutionId, Guid RuntimeInstanceId, CapabilityExecutionScope Scope,
    ContainerRuntimeReceiptData Receipt, ControlCapabilityDefinition Definition, DateTimeOffset Deadline);

public sealed record ControlEvidence(CapabilityExecutionState ExecutionState, ControlResult Result,
    string? ControllerIdentifier, CapabilityExecutionFailure? Failure, DateTimeOffset ObservedAt)
{
    public override string ToString() => $"ControlEvidence {{ ExecutionState = {ExecutionState}, Result = {Result}, Failure = {Failure} }}";
}

public static class ControlEvidenceValidation
{
    public static CapabilityExecutionFailure? Validate(ControlEvidence evidence, DateTimeOffset startedAt,
        DateTimeOffset deadline, DateTimeOffset receivedAt)
    {
        if (!Enum.IsDefined(evidence.ExecutionState) || !Enum.IsDefined(evidence.Result)
            || evidence.Failure is { } failure && !Enum.IsDefined(failure)
            || evidence.ObservedAt == default || evidence.ObservedAt < startedAt
            || evidence.ObservedAt > receivedAt || evidence.ObservedAt > deadline || receivedAt > deadline)
            return CapabilityExecutionFailure.InvalidEvidence;
        if (evidence.ExecutionState == CapabilityExecutionState.PlatformFailed)
            return evidence.Result == ControlResult.Indeterminate && evidence.ControllerIdentifier is null && evidence.Failure is not null
                ? null : CapabilityExecutionFailure.InvalidEvidence;
        if (evidence.ExecutionState != CapabilityExecutionState.Completed || evidence.Failure is not null || evidence.Result == ControlResult.Indeterminate)
            return CapabilityExecutionFailure.InvalidEvidence;
        return evidence.Result switch
        {
            ControlResult.Controlled when evidence.ControllerIdentifier is { Length: > 0 and <= 4096 } identifier
                && !identifier.Contains('\0') => null,
            ControlResult.Uncontrolled when evidence.ControllerIdentifier is null => null,
            _ => CapabilityExecutionFailure.InvalidEvidence
        };
    }
}
