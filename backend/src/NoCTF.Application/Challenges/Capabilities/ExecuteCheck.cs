using NoCTF.Application.Common;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Challenges.Capabilities;

public enum CheckExecutionUse : short { Verification, Observation }

public sealed record CheckExecutionInput(Guid RuntimeInstanceId, RuntimeProvider Provider, string Image,
    IReadOnlyList<string> Command, IReadOnlyDictionary<string, string> Environment, ContainerReceipt TargetReceipt,
    string TargetHost, int TargetReadyTimeoutSeconds, Uri CallbackUrl, string CallbackToken, TimeSpan Timeout,
    bool FixInputEnabled = false, Guid? ScopeId = null)
{
    public CapabilityExecutionScope Scope { get; init; }
    public IReadOnlyList<CheckItemKind> Requirements { get; init; } = [];
    public CheckExecutionUse Use { get; init; }
    public Guid? ExecutionId { get; init; }
    public override string ToString() => $"CheckExecutionInput {{ RuntimeInstanceId = {RuntimeInstanceId}, Scope = {Scope}, ScopeId = {ScopeId}, Provider = {Provider}, Target = [REDACTED], Credentials = [REDACTED] }}";
}

public sealed record CheckExecutionReceipt(Guid ScopeId, Guid RuntimeInstanceId, CapabilityExecutionScope Scope);

public interface ICheckRuntimeExecutor
{
    Task<OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>> ExecuteAsync(CheckExecutionInput input,
        OneShotInputArchive? archive, CancellationToken ct);
}

public sealed class ExecuteCheck(ICheckRuntimeExecutor executor)
{
    public Task<OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>> ExecuteAsync(CheckExecutionInput input,
        OneShotInputArchive? archive, CancellationToken ct)
    {
        if (input.ScopeId is null || input.ScopeId == Guid.Empty || !Enum.IsDefined(input.Scope) || input.RuntimeInstanceId == Guid.Empty
            || input.Provider != input.TargetReceipt.Provider || string.IsNullOrWhiteSpace(input.TargetHost)
            || string.IsNullOrWhiteSpace(input.Image) || string.IsNullOrWhiteSpace(input.CallbackToken)
            || input.CallbackUrl is not { IsAbsoluteUri: true, Scheme: "http" or "https" }
            || input.Timeout <= TimeSpan.Zero || input.Requirements.Count == 0
            || !Enum.IsDefined(input.Use) || input.ExecutionId == Guid.Empty
            || input.Requirements.Any(kind => !Enum.IsDefined(kind)) || input.Requirements.Distinct().Count() != input.Requirements.Count
            || archive is not null && !input.FixInputEnabled)
            return Task.FromResult(OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>.Failure(
                CapabilityExecutionFailure.InvalidInput, "Invalid Check execution requirements or target."));
        return executor.ExecuteAsync(input, archive, ct);
    }
}
