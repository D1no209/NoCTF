using System.Text;
using NoCTF.Application.Common;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public enum ControlReadState { Completed, TimedOut, Unavailable }

public sealed record ControlReadResponse(ControlReadState State, ReadOnlyMemory<byte> Body)
{
    public override string ToString() => $"ControlReadResponse {{ State = {State}, Body = [REDACTED] }}";
}

public interface IControlObservationClient
{
    Task<ControlReadResponse> ReadAsync(Uri target, TimeSpan timeout, CancellationToken ct);
}

public sealed record ControlInput(Guid ExecutionId, Guid? RuntimeInstanceId, ControlCapabilityDefinition Definition,
    Uri? Target, DateTimeOffset Deadline, CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact)
{
    public override string ToString() => $"ControlInput {{ ExecutionId = {ExecutionId}, Scope = {Scope}, RuntimeInstanceId = {RuntimeInstanceId}, Target = [REDACTED] }}";
}

public sealed class ObserveControl(IControlObservationClient client, TimeProvider clock)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<OperationResult<ControlEvaluation, CapabilityExecutionFailure>> ExecuteAsync(ControlInput input, CancellationToken ct)
    {
        if (input.ExecutionId == Guid.Empty || !Enum.IsDefined(input.Scope) || input.Definition.Protocol != ControlProtocol.HttpTeamIdentifier
            || input.Definition.TimeoutSeconds is < 1 or > 3600)
            return OperationResult<ControlEvaluation, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidInput, "Invalid control observation configuration.");
        var remaining = input.Deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) return Outcome(input, ControlResult.Indeterminate, failure: CapabilityExecutionFailure.DeadlineExceeded);
        if (input.Target is not { IsAbsoluteUri: true, Scheme: "http" or "https" })
            return Outcome(input, ControlResult.Indeterminate, failure: CapabilityExecutionFailure.RuntimeUnavailable);
        var timeout = TimeSpan.FromSeconds(input.Definition.TimeoutSeconds);
        var response = await client.ReadAsync(input.Target, remaining < timeout ? remaining : timeout, ct);
        if (response.State != ControlReadState.Completed)
            return Outcome(input, ControlResult.Indeterminate, failure: response.State == ControlReadState.TimedOut
                ? CapabilityExecutionFailure.DeadlineExceeded : CapabilityExecutionFailure.RuntimeUnavailable);
        if (response.Body.Length > 4096 || response.Body.Span.Contains((byte)0))
            return Outcome(input, ControlResult.Indeterminate, failure: CapabilityExecutionFailure.InvalidEvidence);
        if (response.Body.IsEmpty) return Outcome(input, ControlResult.Uncontrolled);
        try { return Outcome(input, ControlResult.Controlled, StrictUtf8.GetString(response.Body.Span)); }
        catch (DecoderFallbackException) { return Outcome(input, ControlResult.Indeterminate, failure: CapabilityExecutionFailure.InvalidEvidence); }
    }

    private OperationResult<ControlEvaluation, CapabilityExecutionFailure> Outcome(ControlInput input, ControlResult result,
        string? controller = null, CapabilityExecutionFailure? failure = null) => OperationResult<ControlEvaluation, CapabilityExecutionFailure>.Success(new()
    {
        ExecutionId = input.ExecutionId, Scope = input.Scope, RuntimeInstanceId = input.RuntimeInstanceId, ObservedAt = clock.GetUtcNow(),
        Result = result, ControllerIdentifier = controller, ExecutionFailure = failure,
        ExecutionState = failure is null ? CapabilityExecutionState.Completed : CapabilityExecutionState.PlatformFailed
    });
}
