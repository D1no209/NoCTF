using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeClaimHandler(
    NoCtfDbContext db,
    IRunnerCapacityGate capacity,
    ITransactionalMessageOutbox outbox,
    IConfiguration configuration)
{
    private static readonly TimeSpan CapacityRetryDelay = TimeSpan.FromSeconds(5);

    public async Task Handle(
        ClaimContainerRuntime message,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(message, cancellationToken);

    public Task<MessageExecutionOutcome> ExecuteAsync(
        ClaimContainerRuntime message,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            message.RuntimeInstanceId,
            message.ProcessingVersion,
            message.Generation,
            message.RunnerPool,
            CapacityLimits(
                message.Definition.Provider,
                message.Definition.Limits,
                podCount: 1),
            scheduledAt => outbox.ScheduleToRunnerPoolAsync(message, scheduledAt),
            (nextVersion, runnerId) => outbox.PublishToRunnerNodeAsync(
                new ProvisionContainerRuntime(
                    message.RuntimeInstanceId,
                    nextVersion,
                    message.Generation,
                    message.RunnerPool,
                    runnerId,
                    message.Definition)),
            cancellationToken);

    public async Task Handle(
        ClaimComposeRuntime message,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(message, cancellationToken);

    public Task<MessageExecutionOutcome> ExecuteAsync(
        ClaimComposeRuntime message,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            message.RuntimeInstanceId,
            message.ProcessingVersion,
            message.Generation,
            message.RunnerPool,
            CapacityLimits(
                message.Definition.Provider,
                message.Definition.Limits,
                message.Definition.ServiceResources.Count),
            scheduledAt => outbox.ScheduleToRunnerPoolAsync(message, scheduledAt),
            (nextVersion, runnerId) => outbox.PublishToRunnerNodeAsync(
                new ProvisionComposeRuntime(
                    message.RuntimeInstanceId,
                    nextVersion,
                    message.Generation,
                    message.RunnerPool,
                    runnerId,
                    message.Definition)),
            cancellationToken);

    public async Task Handle(
        ClaimOvaRuntime message,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(message, cancellationToken);

    public Task<MessageExecutionOutcome> ExecuteAsync(
        ClaimOvaRuntime message,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            message.RuntimeInstanceId,
            message.ProcessingVersion,
            message.Generation,
            message.RunnerPool,
            message.Definition.Limits,
            scheduledAt => outbox.ScheduleToRunnerPoolAsync(message, scheduledAt),
            (nextVersion, runnerId) => outbox.PublishToRunnerNodeAsync(
                new ProvisionOvaRuntime(
                    message.RuntimeInstanceId,
                    nextVersion,
                    message.Generation,
                    message.RunnerPool,
                    runnerId,
                    message.Definition)),
            cancellationToken);

    private RuntimeResourceLimits CapacityLimits(
        RuntimeProvider provider,
        RuntimeResourceLimits configured,
        int podCount)
    {
        if (provider != RuntimeProvider.Kubernetes)
            return configured;
        var text = configuration["Runtime:Kubernetes:PodPidsLimit"];
        if (!long.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var podPidsLimit)
            || podPidsLimit <= 0)
            throw new InvalidOperationException(
                "Runtime:Kubernetes:PodPidsLimit must be configured as a positive integer.");
        return configured with
        {
            PidsLimit = checked(podPidsLimit * podCount)
        };
    }

    private async Task<MessageExecutionOutcome> ExecuteAsync(
        Guid runtimeInstanceId,
        long processingVersion,
        int generation,
        string runnerPool,
        RuntimeResourceLimits limits,
        Func<DateTimeOffset, ValueTask> scheduleRetry,
        Func<long, string, ValueTask> publishProvision,
        CancellationToken cancellationToken)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        if (!string.Equals(runnerPool, configuredPool, StringComparison.Ordinal)
            || RunnerQueueName.FromPool(runnerPool) != RunnerQueueName.FromPool(configuredPool))
        {
            throw new InvalidOperationException(
                "Runtime claim was delivered to the wrong Runner pool.");
        }

        var runnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        var instance = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == runtimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Queued
            || instance.ProcessingVersion != processingVersion
            || instance.Generation != generation)
            return instance is not null
                && instance.State == RuntimeState.Provisioning
                && instance.Generation == generation
                && instance.ProcessingVersion == checked(processingVersion + 1)
                && string.Equals(instance.RunnerId, runnerId, StringComparison.Ordinal)
                ? MessageExecutionOutcome.Idempotent
                : MessageExecutionOutcome.Superseded;

        var claim = await capacity.TryClaimForRunnerAsync(
            new RunnerCapacityRequest(
                runtimeInstanceId,
                runnerPool,
                limits.MemoryBytes,
                limits.NanoCpus,
                limits.PidsLimit),
            runnerId,
            cancellationToken);
        if (claim.Availability != RunnerCapacityAvailability.Claimed)
        {
            await scheduleRetry(DateTimeOffset.UtcNow.Add(CapacityRetryDelay));
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.DeferredCapacity;
        }

        try
        {
            var nextVersion = checked(processingVersion + 1);
            var updated = await db.RuntimeInstances
                .Where(candidate => candidate.Id == runtimeInstanceId
                    && candidate.State == RuntimeState.Queued
                    && candidate.ProcessingVersion == processingVersion
                    && candidate.Generation == generation)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.State, RuntimeState.Provisioning)
                        .SetProperty(candidate => candidate.RunnerId, runnerId)
                        .SetProperty(candidate => candidate.ProcessingVersion, nextVersion),
                    cancellationToken);
            if (updated == 0)
            {
                var assignmentStillOwnsClaim = await db.RuntimeInstances.AsNoTracking().AnyAsync(
                    candidate => candidate.Id == runtimeInstanceId
                        && candidate.RunnerId == runnerId,
                    cancellationToken);
                if (claim.State == RunnerCapacityClaimState.Acquired && !assignmentStillOwnsClaim)
                    await capacity.ReleaseAsync(runtimeInstanceId, runnerId, cancellationToken);
                return MessageExecutionOutcome.Superseded;
            }

            await publishProvision(nextVersion, runnerId);
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.Applied;
        }
        catch
        {
            await capacity.ReleaseAsync(runtimeInstanceId, runnerId, cancellationToken);
            throw;
        }
    }
}
