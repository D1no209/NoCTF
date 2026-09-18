using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProviderHandler(
    IRuntimeProviderCatalog providers,
    IEnumerable<IRuntimeManagedResourceReconciler> resourceReconcilers,
    IOptions<RunnerOptions> runnerOptions,
    IRunnerCapacityGate capacity,
    IRuntimeNodeWorkReader workReader,
    TimeProvider? configuredTimeProvider = null,
    IAwdpAttackProvisioningPlanReader? awdpAttackPlans = null,
    RunnerProviderHealthState? providerHealth = null,
    RunnerResourceMutationCoordinator? mutations = null,
    RunnerResourceObserver? observer = null)
{
    private readonly TimeProvider timeProvider = configuredTimeProvider ?? TimeProvider.System;

    public async Task<object> ProvisionContainerAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        observer?.EnsureFreshAdmission();
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    message.Definition.Provider,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        if (!await capacity.CanCreateAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken))
            throw new TimeoutException("Runtime allocation is waiting for capacity recovery.");
        RuntimeFailureCode? failureCode = null;
        try
        {
            var awdpPlan = awdpAttackPlans is null
                ? new AwdpAttackProvisioningPlan(AwdpAttackProvisioningPlanState.NotApplicable)
                : await awdpAttackPlans.ReadAsync(message, cancellationToken);
            if (awdpPlan.State == AwdpAttackProvisioningPlanState.Invalid)
                throw new RuntimeConfigurationException(
                    "The AWDP attack Runtime provisioning plan is invalid.");
            var definition = awdpPlan.Definition ?? message.Definition;
            var receipt = await IsolatedContainerProvisioner.ProvisionAsync(
                providers.Containers(definition.Provider),
                providers.Sandbox(definition.Provider),
                definition,
                timeProvider.GetUtcNow(),
                cancellationToken);
            providerHealth?.ReportSuccess(definition.Provider);
            ExpandedRuntimeUrls? expanded = null;
            try
            {
                expanded = RuntimeUrlExpander.ExpandContainer(
                    receipt,
                    definition.UrlBindings);
            }
            catch (InvalidOperationException)
            {
                failureCode = RuntimeFailureCode.UrlExpansionFailed;
            }
            if (expanded is not null)
                await capacity.CompleteStartupAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
            if (expanded is not null)
                return new RuntimeProvisioned(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    receipt.Provider,
                    JsonSerializer.Serialize(receipt),
                    expanded.Urls,
                    definition.Ttl is { } ttl ? timeProvider.GetUtcNow().Add(ttl) : null,
                    definition.Provider == RuntimeProvider.Docker
                        ? receipt.PortMappings
                            .OrderBy(mapping => mapping.Key)
                            .Select(mapping => new RuntimePublishedPortMapping(
                                null,
                                mapping.Key,
                                mapping.Value))
                            .ToArray()
                        : null);
        }
        catch (RuntimeConfigurationException)
        {
            failureCode = RuntimeFailureCode.InvalidConfiguration;
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
            providerHealth?.ReportFailure(
                message.Definition.Provider,
                RunnerProviderFailureKind.ProvisionTimedOut,
                message.RuntimeInstanceId);
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
            providerHealth?.ReportFailure(
                message.Definition.Provider,
                RunnerProviderFailureKind.ProvisionRejected,
                message.RuntimeInstanceId);
        }
        return await CompleteProvisionFailureAsync(
            message,
            message.Definition.Provider,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> StopContainerAsync(
        StopContainerRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        RuntimeStopWork? work = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return StopSucceeded(message, work);
            RecordStopQueueDelay(message, work);
            if (work.ProviderReceiptJson is { } receiptJson)
            {
                await RuntimeReceiptCleanup.CleanupContainerAsync(
                    providers,
                    new(message.RuntimeInstanceId),
                    work.Provider,
                    receiptJson,
                    RuntimeTerminationMode.GracefulThenForce,
                    runnerOptions.Value.Cleanup.ToPolicy(),
                    cancellationToken);
            }
            else
            {
                await DestroyUnreceiptedRuntimeAsync(
                    message.RuntimeInstanceId,
                    work,
                    cancellationToken);
            }
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            RecordStopTotal(work, "success", started);
            return StopSucceeded(message, work);
        }
        catch (InvalidOperationException)
        {
            if (work is not null)
            {
                RecordStopTotal(work, "failed", started);
                providerHealth?.ReportFailure(
                    work.Provider,
                    RunnerProviderFailureKind.CleanupFailed,
                    message.RuntimeInstanceId);
            }
            return StopFailed(message, work);
        }
    }

    public async Task<object> ForceTerminateAsync(
        ForceTerminateRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        var started = Stopwatch.GetTimestamp();
        RuntimeStopWork? work = null;
        try
        {
            work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is not null && work.Provider != message.Provider)
            {
                return ForceTerminationFailed(
                    message,
                    timeProvider.GetUtcNow(),
                    RuntimeCleanupResult.CleanupFailed);
            }

            var identity = new RuntimeResourceIdentity(message.RuntimeInstanceId);
            if (work is not null)
                RecordStopQueueDelay(message, work);
            if (work?.ProviderReceiptJson is { } providerReceiptJson)
            {
                await RuntimeReceiptCleanup.CleanupAsync(
                    providers,
                    work.RuntimeKind,
                    identity,
                    message.Provider,
                    providerReceiptJson,
                    RuntimeTerminationMode.Force,
                    runnerOptions.Value.Cleanup.ToPolicy(),
                    cancellationToken);
            }
            else
            {
                var reconciler = ReadResourceReconciler(message.Provider);
                await reconciler.DestroyByIdentityAsync(
                    identity,
                    RuntimeTerminationMode.Force,
                    runnerOptions.Value.Cleanup.ToPolicy(),
                    cancellationToken);
                var remaining = await reconciler.ListManagedAsync(cancellationToken);
                if (remaining.Contains(identity))
                {
                    providerHealth?.ReportFailure(
                        message.Provider,
                        RunnerProviderFailureKind.CleanupFailed,
                        message.RuntimeInstanceId);
                    return ForceTerminationFailed(
                        message,
                        timeProvider.GetUtcNow(),
                        RuntimeCleanupResult.ResourcesRemain);
                }
            }

            var release = await capacity.ReleaseAsync(
                message.RuntimeInstanceId,
                message.RunnerId,
                cancellationToken);
            if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
            {
                return ForceTerminationFailed(
                    message,
                    timeProvider.GetUtcNow(),
                    RuntimeCleanupResult.CapacityOwnershipConflict);
            }

            if (work is not null)
                RecordStopTotal(work, "success", started);
            return ForceTerminationSucceeded(message, timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (work is not null)
                RecordStopTotal(work, "failed", started);
            providerHealth?.ReportFailure(
                message.Provider,
                RunnerProviderFailureKind.CleanupFailed,
                message.RuntimeInstanceId);
            return ForceTerminationFailed(
                message,
                timeProvider.GetUtcNow(),
                RuntimeCleanupResult.CleanupFailed);
        }
    }

    public async Task<object> ProvisionComposeAsync(
        ProvisionComposeRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        observer?.EnsureFreshAdmission();
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    message.Definition.Provider,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        if (!await capacity.CanCreateAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken))
            throw new TimeoutException("Runtime allocation is waiting for capacity recovery.");
        RuntimeFailureCode? failureCode = null;
        try
        {
            var runtime = providers.Compose(message.Definition.Provider);
            var receipt = await runtime.UpAsync(message.Definition, cancellationToken);
            var status = await runtime.GetStatusAsync(receipt, cancellationToken);
            if (status?.Status != RuntimeStatus.Running)
            {
                failureCode = RuntimeFailureCode.ProviderRejected;
                providerHealth?.ReportFailure(
                    message.Definition.Provider,
                    RunnerProviderFailureKind.ProvisionRejected,
                    message.RuntimeInstanceId);
            }
            else
            {
                providerHealth?.ReportSuccess(message.Definition.Provider);
                ExpandedRuntimeUrls? expanded = null;
                try
                {
                    expanded = RuntimeUrlExpander.ExpandCompose(
                        receipt,
                        status,
                        message.Definition.UrlBindings);
                }
                catch (InvalidOperationException)
                {
                    failureCode = RuntimeFailureCode.UrlExpansionFailed;
                }
                if (expanded is not null)
                    await capacity.CompleteStartupAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
                if (expanded is not null)
                    return new RuntimeProvisioned(
                        message.RuntimeInstanceId,
                        message.RunnerId,
                        receipt.Provider,
                        JsonSerializer.Serialize(receipt),
                        expanded.Urls,
                        message.Definition.Ttl is { } ttl
                            ? timeProvider.GetUtcNow().Add(ttl)
                            : null,
                        message.Definition.Provider == RuntimeProvider.Docker
                            ? ReadComposePublishedPorts(message.Definition, status)
                            : null);
            }
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
            providerHealth?.ReportFailure(
                message.Definition.Provider,
                RunnerProviderFailureKind.ProvisionTimedOut,
                message.RuntimeInstanceId);
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
            providerHealth?.ReportFailure(
                message.Definition.Provider,
                RunnerProviderFailureKind.ProvisionRejected,
                message.RuntimeInstanceId);
        }
        return await CompleteProvisionFailureAsync(
            message,
            message.Definition.Provider,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> StopComposeAsync(
        StopComposeRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        RuntimeStopWork? work = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return StopSucceeded(message, work);
            RecordStopQueueDelay(message, work);
            if (work.ProviderReceiptJson is { } receiptJson)
            {
                await RuntimeReceiptCleanup.CleanupComposeAsync(
                    providers,
                    new(message.RuntimeInstanceId),
                    work.Provider,
                    receiptJson,
                    RuntimeTerminationMode.GracefulThenForce,
                    runnerOptions.Value.Cleanup.ToPolicy(),
                    cancellationToken);
            }
            else
            {
                await DestroyUnreceiptedRuntimeAsync(
                    message.RuntimeInstanceId,
                    work,
                    cancellationToken);
            }
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            RecordStopTotal(work, "success", started);
            return StopSucceeded(message, work);
        }
        catch (InvalidOperationException)
        {
            if (work is not null)
            {
                RecordStopTotal(work, "failed", started);
                providerHealth?.ReportFailure(
                    work.Provider,
                    RunnerProviderFailureKind.CleanupFailed,
                    message.RuntimeInstanceId);
            }
            return StopFailed(message, work);
        }
    }

    public async Task<object> ProvisionOvaAsync(
        ProvisionOvaRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        observer?.EnsureFreshAdmission();
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    RuntimeProvider.Libvirt,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }

        if (!await capacity.CanCreateAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken))
            throw new TimeoutException("Runtime allocation is waiting for capacity recovery.");
        RuntimeFailureCode? failureCode = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(message.Definition.OperationTimeout);
        try
        {
            var runtime = providers.Appliance(RuntimeProvider.Libvirt);
            var receipt = await runtime.ImportAsync(message.Definition, timeout.Token);
            providerHealth?.ReportSuccess(RuntimeProvider.Libvirt);
            ExpandedRuntimeUrls? expanded = null;
            try
            {
                expanded = RuntimeUrlExpander.ExpandOva(
                    receipt,
                    message.Definition.UrlBindings);
            }
            catch (InvalidOperationException)
            {
                failureCode = RuntimeFailureCode.UrlExpansionFailed;
            }
            if (expanded is not null)
                await capacity.CompleteStartupAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
            if (expanded is not null)
                return new RuntimeProvisioned(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    receipt.Provider,
                    JsonSerializer.Serialize(receipt),
                    expanded.Urls,
                    message.Definition.Ttl is { } ttl
                        ? timeProvider.GetUtcNow().Add(ttl)
                        : null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
            providerHealth?.ReportFailure(
                RuntimeProvider.Libvirt,
                RunnerProviderFailureKind.ProvisionTimedOut,
                message.RuntimeInstanceId);
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
            providerHealth?.ReportFailure(
                RuntimeProvider.Libvirt,
                RunnerProviderFailureKind.ProvisionTimedOut,
                message.RuntimeInstanceId);
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
            providerHealth?.ReportFailure(
                RuntimeProvider.Libvirt,
                RunnerProviderFailureKind.ProvisionRejected,
                message.RuntimeInstanceId);
        }
        return await CompleteProvisionFailureAsync(
            message,
            RuntimeProvider.Libvirt,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> StopOvaAsync(
        StopOvaRuntime message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterWorkloadAsync(new(RuntimeWorkloadKind.Runtime, message.RuntimeInstanceId, message.RuntimeInstanceId), cancellationToken);
        ValidateAssignment(message);
        RuntimeStopWork? work = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return StopSucceeded(message, work);
            RecordStopQueueDelay(message, work);
            if (work.Provider != RuntimeProvider.Libvirt)
                throw new InvalidOperationException("OVA Runtime receipt provider is invalid.");
            if (work.ProviderReceiptJson is { } receiptJson)
            {
                var receipt = JsonSerializer.Deserialize<OvaRuntimeReceipt>(receiptJson)
                    ?? throw new InvalidOperationException("Provider receipt is invalid.");
                await providers.Appliance(work.Provider).DestroyAsync(
                    receipt,
                    RuntimeTerminationMode.GracefulThenForce,
                    runnerOptions.Value.Cleanup.ToPolicy(),
                    cancellationToken);
            }
            else
            {
                await DestroyUnreceiptedRuntimeAsync(
                    message.RuntimeInstanceId,
                    work,
                    cancellationToken);
            }
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            RecordStopTotal(work, "success", started);
            return StopSucceeded(message, work);
        }
        catch (InvalidOperationException)
        {
            if (work is not null)
            {
                RecordStopTotal(work, "failed", started);
                providerHealth?.ReportFailure(
                    work.Provider,
                    RunnerProviderFailureKind.CleanupFailed,
                    message.RuntimeInstanceId);
            }
            return StopFailed(message, work);
        }
    }

    private void ValidateAssignment(IRunnerNodeMessage message)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
    }

    private async Task<object> CompleteProvisionFailureAsync(
        IRuntimeProvisionMessage message,
        RuntimeProvider provider,
        RuntimeFailureCode failureCode,
        CancellationToken cancellationToken)
    {
        var reconciler = ReadResourceReconciler(provider);
        await reconciler.DestroyByIdentityAsync(
            new RuntimeResourceIdentity(message.RuntimeInstanceId),
            RuntimeTerminationMode.Force,
            runnerOptions.Value.Cleanup.ToPolicy(),
            cancellationToken);
        await ReleaseCapacityOrThrowAsync(message, cancellationToken);
        return new RuntimeProvisionTerminated(
            message.RuntimeInstanceId,
            message.RunnerId,
            failureCode);
    }

    private async Task<RuntimeProvisionCanceled> CancelProvisionAsync(
        IRuntimeProvisionMessage message,
        RuntimeProvider provider,
        CancellationToken cancellationToken)
    {
        var reconciler = ReadResourceReconciler(provider);
        await reconciler.DestroyByIdentityAsync(
            new RuntimeResourceIdentity(message.RuntimeInstanceId),
            RuntimeTerminationMode.Force,
            runnerOptions.Value.Cleanup.ToPolicy(),
            cancellationToken);
        await ReleaseCapacityOrThrowAsync(message, cancellationToken);
        return new RuntimeProvisionCanceled(
            message.RuntimeInstanceId,
            message.RunnerId);
    }

    private IRuntimeManagedResourceReconciler ReadResourceReconciler(
        RuntimeProvider provider) =>
        resourceReconcilers.SingleOrDefault(candidate => candidate.Provider == provider)
        ?? throw new InvalidOperationException(
            $"Runtime resource reconciliation is unavailable for '{provider}'.");

    private async Task DestroyUnreceiptedRuntimeAsync(
        Guid runtimeInstanceId,
        RuntimeStopWork work,
        CancellationToken cancellationToken)
    {
        var reconciler = ReadResourceReconciler(work.Provider);
        var identity = new RuntimeResourceIdentity(runtimeInstanceId);
        await reconciler.DestroyByIdentityAsync(
            identity,
            RuntimeTerminationMode.GracefulThenForce,
            runnerOptions.Value.Cleanup.ToPolicy(),
            cancellationToken);
        var remaining = await reconciler.ListManagedAsync(cancellationToken);
        if (remaining.Contains(identity))
        {
            throw new InvalidOperationException(
                "Runtime resources remain after identity-based cleanup.");
        }
    }

    private static RuntimeStopped StopSucceeded(
        IRuntimeStopMessage message,
        RuntimeStopWork? work) => new(message.RuntimeInstanceId, message.RunnerId);

    private void RecordStopQueueDelay(
        IRuntimeStopMessage message,
        RuntimeStopWork work)
    {
        if (message.RequestedAt == default)
            return;
        NoCtfTelemetry.RecordRuntimeStopQueueDelay(
            work.Provider.ToString(),
            work.RuntimeKind.ToString(),
            Math.Max(0, (timeProvider.GetUtcNow() - message.RequestedAt).TotalSeconds));
    }

    private static void RecordStopTotal(
        RuntimeStopWork work,
        string outcome,
        long started) =>
        NoCtfTelemetry.RecordRuntimeStopDuration(
            work.Provider.ToString(),
            work.RuntimeKind.ToString(),
            "total",
            outcome,
            Stopwatch.GetElapsedTime(started).TotalSeconds);

    private static RuntimeStopFailed StopFailed(
        IRuntimeStopMessage message,
        RuntimeStopWork? work) => new(
            message.RuntimeInstanceId,
            message.RunnerId,
            RuntimeFailureCode.CleanupFailed);

    private static RuntimeForceTerminated ForceTerminationSucceeded(
        ForceTerminateRuntime message,
        DateTimeOffset completedAt) =>
        new(
            message.RuntimeInstanceId,
            message.RunnerId,
            message.ActorUserId,
            message.Reason,
            message.RequestedAt,
            completedAt,
            RuntimeCleanupResult.ResourcesAbsent);

    private static RuntimeForceTerminationFailed ForceTerminationFailed(
        ForceTerminateRuntime message,
        DateTimeOffset completedAt,
        RuntimeCleanupResult result) =>
        new(
            message.RuntimeInstanceId,
            message.RunnerId,
            message.ActorUserId,
            message.Reason,
            message.RequestedAt,
            completedAt,
            result);

    private async Task ReleaseCapacityOrThrowAsync(
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken)
    {
        var release = await capacity.ReleaseAsync(
            message.RuntimeInstanceId,
            message.RunnerId,
            cancellationToken);
        if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
            throw new InvalidOperationException(
                "Runtime capacity belongs to a different Runner assignment.");
    }

    private async Task ReleaseStopCapacityOrThrowAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken)
    {
        var release = await capacity.ReleaseAsync(
            runtimeInstanceId,
            runnerOptions.Value.Id,
            cancellationToken);
        if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
            throw new InvalidOperationException(
                "Runtime capacity belongs to a different Runner assignment.");
    }

    private static IReadOnlyList<RuntimePublishedPortMapping> ReadComposePublishedPorts(
        ComposeRequest request,
        ComposeStatus status)
    {
        var targets = (request.UrlBindings ?? [])
            .Append(request.ControlCheckUrlBinding)
            .Where(binding => binding?.ServiceName is not null
                && binding.ContainerPort is not null)
            .Select(binding => new
            {
                ServiceName = binding!.ServiceName!,
                ContainerPort = binding.ContainerPort!.Value
            })
            .Distinct()
            .OrderBy(target => target.ServiceName, StringComparer.Ordinal)
            .ThenBy(target => target.ContainerPort)
            .ToArray();
        return targets.Select(target =>
        {
            var service = status.Services.Single(candidate => string.Equals(
                candidate.Name,
                target.ServiceName,
                StringComparison.Ordinal));
            if (!service.PublishedPorts.TryGetValue(target.ContainerPort, out var hostPort)
                || hostPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    $"Docker Compose did not publish {target.ServiceName}:{target.ContainerPort}.");
            return new RuntimePublishedPortMapping(
                target.ServiceName,
                target.ContainerPort,
                hostPort);
        }).ToArray();
    }
}

internal static class RuntimeWriteBackOperations
{
    public static async Task ProvisionedAsync(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances
            .Include(candidate => candidate.PublishedPorts)
            .SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null)
            return;
        if (IsLateProvisionSuccessAwaitingCleanup(instance, message))
        {
            instance.ProviderReceiptJson ??= message.ProviderReceiptJson;
            await ReplacePublishedPortsAsync(
                instance, message, events, timeProvider.GetUtcNow(), cancellationToken);
            await InvalidateAwdpAttackFlagAsync(db, instance, timeProvider.GetUtcNow(), cancellationToken);
            await EndChallengeTestFlagAsync(
                db, instance, RuntimeTestFlagState.Canceled, timeProvider.GetUtcNow(), cancellationToken);
            await PublishRuntimeStopAsync(
                outbox,
                instance,
                message.RunnerId,
                timeProvider.GetUtcNow());
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                instance,
                db,
                outbox,
                events,
                timeProvider.GetUtcNow(),
                AwdpFixRuntimeCleanupMode.CallerManaged,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        if (!string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.RuntimeProvider != message.Provider)
            return;

        if (!TryReadAccessDisplays(message.Urls, out var accessDisplays))
        {
            instance.ProviderReceiptJson = message.ProviderReceiptJson;
            instance.Urls = [];
            instance.State = RuntimeState.Stopping;
            await InvalidateAwdpAttackFlagAsync(db, instance, timeProvider.GetUtcNow(), cancellationToken);
            await EndChallengeTestFlagAsync(
                db, instance, RuntimeTestFlagState.Failed, timeProvider.GetUtcNow(), cancellationToken);
            await PublishRuntimeStopAsync(
                outbox,
                instance,
                message.RunnerId,
                timeProvider.GetUtcNow());
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                instance,
                db,
                outbox,
                events,
                timeProvider.GetUtcNow(),
                AwdpFixRuntimeCleanupMode.CallerManaged,
                cancellationToken);
            await RecordRuntimeStateAsync(
                events,
                instance,
                CompetitionEventLevel.Error,
                timeProvider.GetUtcNow(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var runningAt = timeProvider.GetUtcNow();
        instance.ProviderReceiptJson = message.ProviderReceiptJson;
        instance.Urls = accessDisplays;
        instance.State = RuntimeState.Running;
        instance.FailureCode = null;
        instance.RunningAt = runningAt;
        instance.ExpiresAt = message.ExpiresAt;
        await ActivateAwdpAttackFlagAsync(db, instance, runningAt, cancellationToken);
        await StartChallengeTestFlagDeliveryAsync(
            db, outbox, instance, message.RunnerId, runningAt, cancellationToken);
        await ReplacePublishedPortsAsync(
            instance, message, events, runningAt, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var currentAwdFlag = instance.TeamId is null
            ? null
            : await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId == instance.CompetitionChallengeId
                    && flag.TeamId == instance.TeamId
                    && flag.SpecificationKind == NoCTF.Domain.Challenges.SpecificationKind.AwdRound
                    && flag.ValidStart <= now
                    && flag.ValidUntil > now
                    && flag.DeletedAt == null)
                .SingleOrDefaultAsync(cancellationToken);
        if (currentAwdFlag is not null)
        {
            await outbox.PublishToRunnerNodeAsync(new InjectAwdFlag(
                instance.Id,
                instance.CompetitionChallengeId!.Value,
                currentAwdFlag.Id,
                currentAwdFlag.ValidUntil!.Value,
                message.RunnerId));
        }
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.RunningAt ?? now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ProvisionFailedAsync(
        RuntimeProvisionFailed message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox? outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || !string.Equals(
                instance.RunnerId,
                message.RunnerId,
                StringComparison.Ordinal))
            return;

        await PersistProvisionFailureAsync(
            instance,
            message.FailureCode,
            db,
            events,
            outbox,
            timeProvider,
            cancellationToken);
    }

    public static async Task ProvisionTerminatedAsync(
        RuntimeProvisionTerminated message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.ProviderReceiptJson is not null)
            return;

        if (instance.State == RuntimeState.Provisioning)
        {
            await PersistProvisionFailureAsync(
                instance,
                message.FailureCode,
                db,
                events,
                outbox,
                timeProvider,
                cancellationToken);
            return;
        }
        if (instance.State != RuntimeState.Stopping)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = timeProvider.GetUtcNow();
        await InvalidateAwdpAttackFlagAsync(db, instance, instance.StoppedAt.Value, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Canceled, instance.StoppedAt.Value, cancellationToken);
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            instance.StoppedAt.Value,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async Task PersistProvisionFailureAsync(
        RuntimeInstance instance,
        RuntimeFailureCode failureCode,
        NoCtfDbContext db,
        ICompetitionEventRecorder events,
        ITransactionalMessageOutbox? outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        instance.State = RuntimeState.Failed;
        instance.FailureCode = failureCode;
        var failedAt = timeProvider.GetUtcNow();
        await InvalidateAwdpAttackFlagAsync(db, instance, failedAt, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Failed, failedAt, cancellationToken);
        var effectiveOutbox = outbox ?? new NoOpTransactionalMessageOutbox();
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            effectiveOutbox,
            events,
            failedAt,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Error,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (outbox is not null)
            await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task StoppedAsync(
        RuntimeStopped message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Stopping
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = timeProvider.GetUtcNow();
        await InvalidateAwdpAttackFlagAsync(db, instance, instance.StoppedAt.Value, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Canceled, instance.StoppedAt.Value, cancellationToken);
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            instance.StoppedAt.Value,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ForceTerminatedAsync(
        RuntimeForceTerminated message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Stopping
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = message.CompletedAt;
        await InvalidateAwdpAttackFlagAsync(db, instance, instance.StoppedAt.Value, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Canceled, instance.StoppedAt.Value, cancellationToken);
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            message.CompletedAt,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        if (instance.CompetitionId is Guid competitionId)
        {
            await events.RecordAsync(new(
                competitionId,
                CompetitionEventKind.RuntimeForceTerminationCompleted,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                message.CompletedAt,
                ActorUserId: message.ActorUserId,
                TeamId: instance.TeamId,
                CompetitionChallengeId: instance.CompetitionChallengeId,
                RuntimeInstanceId: instance.Id,
                RuntimeState: instance.State,
                RuntimeCleanupResult: message.CleanupResult,
                Reason: message.Reason), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ForceTerminationFailedAsync(
        RuntimeForceTerminationFailed message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.RuntimeInstanceId,
                cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Stopping
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return;

        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            message.CompletedAt,
            AwdpFixRuntimeCleanupMode.EnsureStop,
            cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Failed, message.CompletedAt, cancellationToken);
        if (instance.CompetitionId is Guid competitionId)
        {
            await events.RecordAsync(new(
                competitionId,
                CompetitionEventKind.RuntimeForceTerminationFailed,
                CompetitionEventLevel.Error,
                CompetitionEventVisibility.Staff,
                message.CompletedAt,
                ActorUserId: message.ActorUserId,
                TeamId: instance.TeamId,
                CompetitionChallengeId: instance.CompetitionChallengeId,
                RuntimeInstanceId: instance.Id,
                RuntimeState: instance.State,
                RuntimeCleanupResult: message.CleanupResult,
                Reason: message.Reason), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task StopFailedAsync(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Stopping
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return;

        instance.State = RuntimeState.Failed;
        instance.FailureCode = message.FailureCode;
        var failedAt = timeProvider.GetUtcNow();
        await InvalidateAwdpAttackFlagAsync(db, instance, failedAt, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Failed, failedAt, cancellationToken);
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            failedAt,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Error,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ProvisionCanceledAsync(
        RuntimeProvisionCanceled message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Stopping
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.ProviderReceiptJson is not null)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = timeProvider.GetUtcNow();
        await InvalidateAwdpAttackFlagAsync(db, instance, instance.StoppedAt.Value, cancellationToken);
        await EndChallengeTestFlagAsync(
            db, instance, RuntimeTestFlagState.Canceled, instance.StoppedAt.Value, cancellationToken);
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            instance,
            db,
            outbox,
            events,
            instance.StoppedAt.Value,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async ValueTask<Guid?> RecordRuntimeStateAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance instance,
        CompetitionEventLevel level,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (instance.CompetitionId is not Guid competitionId)
            return null;
        return await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.RuntimeStateChanged,
            level,
            instance.TeamId is null
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: instance.TeamId,
            CompetitionChallengeId: instance.CompetitionChallengeId,
            RuntimeInstanceId: instance.Id,
            GameplayFactId: instance.GameplayFactId,
            RuntimeState: instance.State),
            cancellationToken);
    }

    private static async Task StartChallengeTestFlagDeliveryAsync(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        RuntimeInstance instance,
        string runnerId,
        DateTimeOffset runningAt,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose != RuntimePurpose.TemplateTest
            || instance.TestFlagState != RuntimeTestFlagState.Pending)
            return;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.ChallengeId == instance.ChallengeId
                && candidate.SpecificationKind
                    == NoCTF.Domain.Challenges.SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == instance.Id
                && candidate.DeletedAt == null,
            cancellationToken);
        if (flag is null)
        {
            instance.TestFlagState = RuntimeTestFlagState.Failed;
            return;
        }

        if (instance.TestFlagDelivery == RuntimeTestFlagDelivery.Environment)
        {
            flag.ValidStart ??= runningAt;
            instance.TestFlagState = RuntimeTestFlagState.Succeeded;
            return;
        }
        if (instance.TestFlagDelivery == RuntimeTestFlagDelivery.Command
            && instance.ChallengeId is Guid challengeId)
        {
            await outbox.PublishToRunnerNodeAsync(new InjectChallengeTestFlag(
                instance.Id,
                challengeId,
                flag.Id,
                runnerId));
        }
    }

    private static async Task EndChallengeTestFlagAsync(
        NoCtfDbContext db,
        RuntimeInstance instance,
        RuntimeTestFlagState state,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose != RuntimePurpose.TemplateTest
            || instance.TestFlagDelivery == RuntimeTestFlagDelivery.NotRequired)
            return;
        if (instance.TestFlagState == RuntimeTestFlagState.Pending)
            instance.TestFlagState = state;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.ChallengeId == instance.ChallengeId
                && candidate.SpecificationKind
                    == NoCTF.Domain.Challenges.SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == instance.Id
                && candidate.DeletedAt == null,
            cancellationToken);
        if (flag is not null)
            flag.ValidUntil ??= at;
    }

    private static async Task ActivateAwdpAttackFlagAsync(
        NoCtfDbContext db,
        RuntimeInstance instance,
        DateTimeOffset runningAt,
        CancellationToken cancellationToken)
    {
        if (!await IsAwdpPlayerRuntimeAsync(db, instance, cancellationToken))
            return;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.SpecificationKind
                    == NoCTF.Domain.Challenges.SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == instance.Id
                && candidate.CompetitionChallengeId == instance.CompetitionChallengeId
                && candidate.TeamId == instance.TeamId
                && candidate.DeletedAt == null,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The AWDP attack Runtime flag is unavailable.");
        flag.ValidStart ??= runningAt;
        flag.ValidUntil = null;
    }

    private static async Task InvalidateAwdpAttackFlagAsync(
        NoCtfDbContext db,
        RuntimeInstance instance,
        DateTimeOffset stoppedAt,
        CancellationToken cancellationToken)
    {
        if (!await IsAwdpPlayerRuntimeAsync(db, instance, cancellationToken))
            return;
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.SpecificationKind
                    == NoCTF.Domain.Challenges.SpecificationKind.RuntimeInstance
                && candidate.SpecificationId == instance.Id
                && candidate.DeletedAt == null,
            cancellationToken);
        if (flag is not null && (flag.ValidUntil is null || flag.ValidUntil > stoppedAt))
            flag.ValidUntil = stoppedAt;
    }

    private static Task<bool> IsAwdpPlayerRuntimeAsync(
        NoCtfDbContext db,
        RuntimeInstance instance,
        CancellationToken cancellationToken) =>
        instance.Purpose is RuntimePurpose.Player or RuntimePurpose.AwdpAttack
            ? db.Competitions.AsNoTracking().AnyAsync(
                competition => competition.Id == instance.CompetitionId
                    && competition.Mode == GameMode.Awdp,
                cancellationToken)
            : Task.FromResult(false);

    private static bool IsLateProvisionSuccessAwaitingCleanup(
        RuntimeInstance instance,
        RuntimeProvisioned message) =>
        instance.State == RuntimeState.Stopping
        && instance.ProviderReceiptJson is null
        && instance.RuntimeProvider == message.Provider
        && string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal);

    private static async Task ReplacePublishedPortsAsync(
        RuntimeInstance instance,
        RuntimeProvisioned message,
        ICompetitionEventRecorder events,
        DateTimeOffset allocatedAt,
        CancellationToken cancellationToken)
    {
        if (message.PublishedPorts is null)
            return;
        if (message.Provider != RuntimeProvider.Docker && message.PublishedPorts.Count > 0)
            throw new InvalidOperationException(
                "Only Docker runtime results may contain host published ports.");
        var mappings = message.PublishedPorts
            .OrderBy(mapping => mapping.ServiceName, StringComparer.Ordinal)
            .ThenBy(mapping => mapping.ContainerPort)
            .ToArray();
        if (mappings.Any(mapping => mapping.ContainerPort is < 1 or > 65535
                || mapping.HostPort is < 1 or > 65535)
            || mappings.Select(mapping => (mapping.ServiceName, mapping.ContainerPort))
                .Distinct()
                .Count() != mappings.Length)
            throw new InvalidOperationException("Runtime published port results are invalid.");

        instance.PublishedPorts.Clear();
        foreach (var mapping in mappings)
        {
            instance.PublishedPorts.Add(new RuntimePublishedPort
            {
                ServiceName = mapping.ServiceName,
                ContainerPort = mapping.ContainerPort,
                HostPort = mapping.HostPort
            });
            if (instance.CompetitionId is Guid competitionId)
            {
                await events.RecordAsync(new(
                    competitionId,
                    CompetitionEventKind.RuntimePortAllocated,
                    CompetitionEventLevel.Information,
                    instance.TeamId is null
                        ? CompetitionEventVisibility.Public
                        : CompetitionEventVisibility.Team,
                    allocatedAt,
                    TeamId: instance.TeamId,
                    CompetitionChallengeId: instance.CompetitionChallengeId,
                    RuntimeInstanceId: instance.Id,
                    GameplayFactId: instance.GameplayFactId,
                    RuntimeState: instance.State,
                    HostPort: mapping.HostPort), cancellationToken);
            }
        }
    }

    private static ValueTask PublishRuntimeStopAsync(
        ITransactionalMessageOutbox outbox,
        RuntimeInstance instance,
        string runnerId,
        DateTimeOffset requestedAt) =>
        instance.RuntimeKind switch
        {
            RuntimeKind.Container => outbox.PublishToRunnerNodeAsync(
                new StopContainerRuntime(
                    instance.Id,
                    runnerId,
                    requestedAt)),
            RuntimeKind.Compose => outbox.PublishToRunnerNodeAsync(
                new StopComposeRuntime(
                    instance.Id,
                    runnerId,
                    requestedAt)),
            RuntimeKind.OvaVm => outbox.PublishToRunnerNodeAsync(
                new StopOvaRuntime(
                    instance.Id,
                    runnerId,
                    requestedAt)),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime kind '{instance.RuntimeKind}'.")
        };

    private static bool TryReadAccessDisplays(
        IReadOnlyList<string> values,
        out string[] accessDisplays)
    {
        accessDisplays = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
                return false;
            accessDisplays[index] = values[index].Trim();
        }

        return true;
    }
}
