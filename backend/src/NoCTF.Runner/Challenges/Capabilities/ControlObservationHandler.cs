using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Runner.Challenges.Capabilities;

[NonTransactional]
public sealed class ControlObservationHandler(ControlExecutionStore executions, IOneShotRuntimeProviderCatalog providers,
    AuxiliaryRuntimeCapacity capacity, IRunnerCapacityGate admission, RunnerResourceMutationCoordinator mutations,
    IRunnerScoringTokenIssuer tokens, IOptions<RunnerOptions> runner, IOptions<RunnerScoringOptions> scoring,
    RuntimeExecutionOptions limits, TimeProvider clock, IHostApplicationLifetime lifetime,
    ILogger<ControlObservationHandler> logger)
{
    public async Task Handle(RunControlObservation command, CancellationToken ct)
    {
        RunnerNodeAssignmentGuard.Validate(command, runner.Value.Pool, runner.Value.Id);
        using var targetMutation = await mutations.EnterRuntimeAsync(command.RuntimeInstanceId, ct);
        var work = await executions.ClaimAsync(command, ct);
        if (work is null) return;
        CapabilityExecutionFailure? failure = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = work.Deadline - clock.GetUtcNow();
            var executionBudget = TimeSpan.FromSeconds(work.Definition.TimeoutSeconds);
            if (remaining > executionBudget) remaining = executionBudget;
            if (remaining <= TimeSpan.Zero) deadline.Cancel(); else deadline.CancelAfter(remaining);
            if (!await admission.CanMutateAsync(work.RuntimeInstanceId, runner.Value.Id, deadline.Token))
                failure = CapabilityExecutionFailure.TargetSuperseded;
            else
            {
                var definition = work.Definition;
                var host = work.Receipt.Services.Single(value => value.Name == definition.ServiceName).InternalHost
                    ?? throw new InvalidOperationException("The Control target has no internal address.");
                var origin = scoring.Value.CallbackBaseUrl ?? throw new InvalidOperationException("Runner callback origin is required.");
                var environment = definition.Environment.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
                environment["TARGET_HOST"] = host; environment["NOCTF_TARGET_HOST"] = host;
                environment["NOCTF_EXECUTION_SCOPE"] = work.Scope.ToString();
                environment["NOCTF_CALLBACK_URL"] = new Uri(origin, "/api/internal/v1/capabilities/control-results").AbsoluteUri;
                environment["NOCTF_CALLBACK_TOKEN"] = tokens.IssueControlResult(runner.Value.Id, work.ExecutionId,
                    work.RuntimeInstanceId, work.Scope, work.Deadline, clock.GetUtcNow());
                var request = new ContainerRequest(work.ExecutionId, work.Receipt.Provider, definition.Image!,
                    definition.Command.OrderBy(value => value.Position).Select(value => value.Value).ToArray(), environment,
                    new Dictionary<string, string>(), new Dictionary<int, int>(),
                    new RuntimeResourceLimits(256 * 1024 * 1024, 250, limits.ProcessesPerService), remaining,
                    OperationTimeout: remaining, AllowInternalCallback: true, RuntimeInstanceId: work.RuntimeInstanceId,
                    NetworkPurpose: ContainerNetworkPurpose.ControlObserver);
                var kind = work.Scope == CapabilityExecutionScope.TemplateTest ? RuntimeWorkloadKind.TemplateChecker : RuntimeWorkloadKind.ControlObserver;
                var identity = new RuntimeWorkloadIdentity(kind, work.RuntimeInstanceId, work.ExecutionId);
                var target = new AttachedContainerRuntimeTarget(new(work.RuntimeInstanceId), work.Receipt.ServiceReceipt(definition.ServiceName!));
                using var finished = new CancellationTokenSource();
                var monitor = MonitorAsync(command, deadline, finished.Token);
                try
                {
                    var result = await capacity.RunAsync(request, identity, work.Scope == CapabilityExecutionScope.GameplayFact ? work.ExecutionId : null,
                        (reserved, token) => providers.Attached(request.Provider).RunAttachedAsync(reserved, target, null, token), deadline.Token);
                    if (result.ExitCode != 0) failure = CapabilityExecutionFailure.TargetExecutionFailed;
                }
                finally
                {
                    await finished.CancelAsync();
                    try { if (await monitor) failure = CapabilityExecutionFailure.TargetSuperseded; }
                    catch (OperationCanceledException) when (finished.IsCancellationRequested) { }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { failure ??= CapabilityExecutionFailure.DeadlineExceeded; }
        catch (OneShotCleanupException) { failure = CapabilityExecutionFailure.ResourceCleanupFailed; }
        catch (RunnerCapacityUnavailableException) { failure = CapabilityExecutionFailure.ResourceUnavailable; }
        catch (Exception exception)
        {
            failure = CapabilityExecutionFailure.ExecutionInterrupted;
            logger.LogError("Control execution {ExecutionId} failed with {FailureType}.", command.ExecutionId, exception.GetType().Name);
        }
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        completion.CancelAfter(TimeSpan.FromSeconds(30));
        await executions.CompleteAsync(command, failure, completion.Token);
    }

    private async Task<bool> MonitorAsync(RunControlObservation command, CancellationTokenSource execution, CancellationToken stopped)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stopped))
                if (!await executions.IsCurrentAsync(command, stopped))
                {
                    await execution.CancelAsync();
                    return true;
                }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        catch { await execution.CancelAsync(); throw; }
        return false;
    }
}
