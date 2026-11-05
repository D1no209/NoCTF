using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Runner.Composition;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Application.Runtime.Capacity;
using Microsoft.Extensions.Options;

namespace NoCTF.Runner.Challenges.Capabilities;

public sealed class RuntimeFixExecutor(IRuntimeProviderCatalog providers, RunnerResourceMutationCoordinator mutations,
    IRunnerCapacityGate capacity, IOptions<RunnerOptions> options, IFixExecutionProgressStore progress, TimeProvider clock) : IFixRuntimeExecutor
{
    public async Task<FixRuntimeResult> ExecuteAsync(FixRuntimeInput input, CancellationToken ct)
    {
        using var mutation = await mutations.EnterAsync(ct);
        var runtimeId = input.Target.RuntimeInstanceId ?? input.Target.OperationId;
        if (!await capacity.CanMutateAsync(runtimeId, options.Value.Id, ct)) return new(null, false, CapabilityExecutionFailure.TargetSuperseded);
        if (!await progress.RecordMutationStartedAsync(input.ExecutionId, runtimeId, input.TargetKind, clock.GetUtcNow(), ct, input.Scope))
            return new(null, false, CapabilityExecutionFailure.TargetSuperseded);
        var sandbox = providers.Sandbox(input.Target.Provider);
        try { await sandbox.CopyArchiveAsync(input.Target, input.PreparedArchive, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(null, false, CapabilityExecutionFailure.InputPreparationFailed, input.TargetKind == FixTargetKind.Persistent); }
        if (!await capacity.CanMutateAsync(runtimeId, options.Value.Id, ct)) return new(null, false, CapabilityExecutionFailure.TargetSuperseded);
        try
        {
            var result = input.TargetKind == FixTargetKind.Persistent
                ? await sandbox.ExecRetainingTargetAsync(input.Target, input.Command, input.Timeout, ct)
                : await sandbox.ExecAsync(input.Target, input.Command, input.Timeout, ct);
            return new(result.ExitCode, result.TimedOut, result.PlatformFailure || result.ExecutionUncertain && (!result.TimedOut || ct.IsCancellationRequested)
                    ? CapabilityExecutionFailure.ExecutionInterrupted : null,
                result.ExecutionUncertain, result.ExecutionIdentity);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new(null, false, CapabilityExecutionFailure.TargetExecutionFailed, input.TargetKind == FixTargetKind.Persistent); }
    }
}
