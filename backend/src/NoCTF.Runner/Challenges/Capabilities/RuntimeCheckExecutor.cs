using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Common;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Runner.Challenges.Capabilities;

public sealed class RuntimeCheckExecutor(IOneShotRuntimeProviderCatalog providers, RuntimeExecutionOptions executionOptions,
    AuxiliaryRuntimeCapacity? capacity = null) : ICheckRuntimeExecutor
{
    public async Task<OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>> ExecuteAsync(CheckExecutionInput input,
        OneShotInputArchive? archive, CancellationToken ct)
    {
        var scopeId = input.ScopeId ?? throw new ArgumentException("A Check workload requires its execution scope.", nameof(input));
        var environment = new Dictionary<string, string>(input.Environment, StringComparer.Ordinal)
        {
            ["TARGET_HOST"] = input.TargetHost,
            ["NOCTF_TARGET_HOST"] = input.TargetHost,
            ["NOCTF_CHECK_REQUIREMENTS"] = JsonSerializer.Serialize(input.Requirements.Select(kind => kind.ToString())),
            ["TARGET_READY_TIMEOUT_SECONDS"] = input.TargetReadyTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NOCTF_CALLBACK_URL"] = input.CallbackUrl.AbsoluteUri,
            ["NOCTF_CALLBACK_TOKEN"] = input.CallbackToken
        };
        var request = new ContainerRequest(input.ExecutionId ?? CreateOperationId(scopeId, input.RuntimeInstanceId), input.Provider,
            input.Image, input.Command, environment, new Dictionary<string, string>(), new Dictionary<int, int>(),
            new RuntimeResourceLimits(256 * 1024 * 1024, 250, executionOptions.ProcessesPerService), input.Timeout,
            OperationTimeout: input.Timeout, AllowInternalCallback: true, RuntimeInstanceId: input.RuntimeInstanceId,
            NetworkPurpose: input.Use == CheckExecutionUse.Observation ? ContainerNetworkPurpose.AwdChecker : ContainerNetworkPurpose.AwdpVerification);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(input.Timeout);
        try
        {
            var runner = providers.Attached(input.Provider);
            var target = new AttachedContainerRuntimeTarget(new(input.RuntimeInstanceId), input.TargetReceipt);
            OneShotResult result;
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    result = capacity is null ? await runner.RunAttachedAsync(request, target, archive, timeout.Token)
                        : await capacity.RunAsync(request, new(input.Scope == CapabilityExecutionScope.TemplateTest ? RuntimeWorkloadKind.TemplateChecker
                            : input.Use == CheckExecutionUse.Observation ? RuntimeWorkloadKind.AwdChecker : RuntimeWorkloadKind.PatchChecker,
                            input.RuntimeInstanceId, request.OperationId), input.Scope == CapabilityExecutionScope.GameplayFact ? scopeId : null,
                            (reserved, token) => runner.RunAttachedAsync(reserved, target, archive, token), timeout.Token);
                    break;
                }
                catch (RunnerCapacityUnavailableException exception) when (exception.Failure == RunnerAdmissionFailure.StartupConcurrencyLimited)
                { await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token); }
            }
            return result.ExitCode == 0
                ? OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>.Success(new(scopeId, input.RuntimeInstanceId, input.Scope))
                : Failure(CapabilityExecutionFailure.TargetExecutionFailed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failure(archive is { PreparationCompleted: false } ? CapabilityExecutionFailure.InputPreparationFailed : CapabilityExecutionFailure.DeadlineExceeded);
        }
        catch (OneShotInputPreparationException) { return Failure(CapabilityExecutionFailure.InputPreparationFailed); }
        catch (RunnerCapacityUnavailableException) { return Failure(CapabilityExecutionFailure.ResourceUnavailable); }
        catch (OneShotCleanupException) { return Failure(CapabilityExecutionFailure.ResourceCleanupFailed); }
        catch (Exception) { return Failure(CapabilityExecutionFailure.TargetExecutionFailed); }
    }

    private static OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure> Failure(CapabilityExecutionFailure code) =>
        OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure>.Failure(code, "The Check workload did not complete successfully.");

    private static Guid CreateOperationId(Guid factId, Guid runtimeId)
    {
        Span<byte> input = stackalloc byte[40];
        factId.TryWriteBytes(input[..16]); runtimeId.TryWriteBytes(input[16..32]);
        BinaryPrimitives.WriteInt64BigEndian(input[32..], 0x636865636b);
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(input, hash);
        return new(hash[..16]);
    }
}
