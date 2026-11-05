using NoCTF.Application.Common;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public sealed record FixInput(Guid ExecutionId, Guid RuntimeInstanceId, FixCapabilityDefinition Definition,
    ContainerReceipt Target, Stream PreparedArchive, DateTimeOffset Deadline, FixTargetKind TargetKind = FixTargetKind.Isolated,
    CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact);
public sealed record FixRuntimeInput(Guid ExecutionId, ContainerReceipt Target, Stream PreparedArchive, IReadOnlyList<string> Command, TimeSpan Timeout,
    FixTargetKind TargetKind = FixTargetKind.Isolated, CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact);
public sealed record FixRuntimeResult(int? ExitCode, bool TimedOut, CapabilityExecutionFailure? Failure = null,
    bool ExecutionUncertain = false, string? ExecutionIdentity = null);

public interface IFixRuntimeExecutor
{
    Task<FixRuntimeResult> ExecuteAsync(FixRuntimeInput input, CancellationToken ct);
}

public interface IFixExecutionProgressStore
{
    Task<bool> RecordMutationStartedAsync(Guid executionId, Guid runtimeId, FixTargetKind targetKind, DateTimeOffset now, CancellationToken ct,
        CapabilityExecutionScope scope = CapabilityExecutionScope.GameplayFact);
    Task<bool> RecordAppliedAsync(FixOutcome outcome, CancellationToken ct);
}

public sealed class ExecuteFix(IFixRuntimeExecutor executor, TimeProvider clock)
{
    public async Task<OperationResult<FixExecutionResult, CapabilityExecutionFailure>> ExecuteAsync(FixInput input, CancellationToken ct)
    {
        if (input.ExecutionId == Guid.Empty || !Enum.IsDefined(input.Scope) || input.RuntimeInstanceId == Guid.Empty || !input.PreparedArchive.CanRead
            || input.Definition.TimeoutSeconds is < 1 or > 3600 || !FixCommand.IsValid(input.Definition))
            return OperationResult<FixExecutionResult, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidInput, "Invalid Fix target, command or prepared archive.");
        var remaining = input.Deadline - clock.GetUtcNow();
        FixRuntimeResult execution;
        if (remaining <= TimeSpan.Zero) execution = new(null, false, CapabilityExecutionFailure.DeadlineExceeded);
        else
        {
            var timeout = TimeSpan.FromSeconds(input.Definition.TimeoutSeconds);
            execution = await executor.ExecuteAsync(new(input.ExecutionId, input.Target, input.PreparedArchive, FixCommand.Create(input.Definition),
                timeout < remaining ? timeout : remaining, input.TargetKind, input.Scope), ct);
        }
        return OperationResult<FixExecutionResult, CapabilityExecutionFailure>.Success(new()
        {
            ExecutionId = input.ExecutionId, Scope = input.Scope, RuntimeInstanceId = input.RuntimeInstanceId, ObservedAt = clock.GetUtcNow(),
            ExecutionState = execution.Failure is null ? CapabilityExecutionState.Completed : CapabilityExecutionState.PlatformFailed,
            ExecutionFailure = execution.Failure, ExitCode = execution.ExitCode,
            ExecutionUncertain = execution.ExecutionUncertain, ExecutionIdentity = execution.ExecutionIdentity,
            Result = execution.Failure is not null ? null : execution.TimedOut ? FixResult.TimedOut : execution.ExitCode == 0 ? FixResult.Applied : FixResult.Failed
        });
    }
}

public static class FixCommand
{
    public const string EntrypointPlaceholder = "{entrypoint}";
    public const int MaximumArguments = 64;
    public const int MaximumArgumentLength = 4096;

    public static bool IsValid(FixCapabilityDefinition definition) => !string.IsNullOrWhiteSpace(definition.Entrypoint)
        && definition.Entrypoint.Length <= 256 && !Path.IsPathRooted(definition.Entrypoint)
        && !definition.Entrypoint.Contains('\0') && !definition.Entrypoint.Contains(':')
        && !definition.Entrypoint.Split('/', '\\').Any(part => part is "." or ".." or "")
        && definition.Command.Count <= MaximumArguments
        && definition.Command.All(argument => !string.IsNullOrWhiteSpace(argument.Value) && argument.Value.Length <= MaximumArgumentLength && !argument.Value.Contains('\0'))
        && (definition.Command.Count == 0 || definition.Command.Count(argument => argument.Value == EntrypointPlaceholder) == 1);

    public static IReadOnlyList<string> Create(FixCapabilityDefinition definition)
    {
        if (!IsValid(definition)) throw new InvalidOperationException("Invalid Fix command.");
        var entrypoint = $"/noctf/fix/{definition.Entrypoint.Replace('\\', '/')}";
        return definition.Command.Count == 0 ? ["/bin/sh", entrypoint]
            : definition.Command.OrderBy(argument => argument.Position).Select(argument => argument.Value == EntrypointPlaceholder ? entrypoint : argument.Value).ToArray();
    }
}
