using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Challenges.Images;
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
            ChallengeImagePinningPolicy.IsPinnedImage(message.Definition.Image),
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
            ChallengeImagePinningPolicy.AreComposeImagesPinned(
                message.Definition.ComposeYaml),
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
            definitionPinned: true,
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
        var podPidsLimit = ReadPositiveLong(
            "Runtime:Kubernetes:PodPidsLimit",
            defaultValue: null);
        return configured with
        {
            PidsLimit = checked(podPidsLimit * podCount)
        };
    }

    private long ReadPositiveLong(string key, long? defaultValue)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text) && defaultValue is long fallback)
            return fallback;
        if (!long.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
            throw new InvalidOperationException(
                $"{key} must be configured as a positive integer.");
        return parsed;
    }

    private async Task<MessageExecutionOutcome> ExecuteAsync(
        Guid runtimeInstanceId,
        long processingVersion,
        int generation,
        string runnerPool,
        bool definitionPinned,
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

        if (!definitionPinned)
        {
            var nextVersion = checked(processingVersion + 1);
            var assignmentReleaseToken = Guid.CreateVersion7();
            int rejected;
            if (db.Database.IsRelational())
            {
                rejected = await db.RuntimeInstances
                    .Where(candidate => candidate.Id == runtimeInstanceId
                        && candidate.State == RuntimeState.Queued
                        && candidate.ProcessingVersion == processingVersion
                        && candidate.Generation == generation)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(candidate => candidate.State, RuntimeState.Provisioning)
                            .SetProperty(candidate => candidate.RunnerId, runnerId)
                            .SetProperty(
                                candidate => candidate.RunnerAssignmentReleaseToken,
                                assignmentReleaseToken)
                            .SetProperty(candidate => candidate.ProcessingVersion, nextVersion),
                        cancellationToken);
            }
            else
            {
                var tracked = await db.RuntimeInstances.SingleOrDefaultAsync(
                    candidate => candidate.Id == runtimeInstanceId
                        && candidate.State == RuntimeState.Queued
                        && candidate.ProcessingVersion == processingVersion
                        && candidate.Generation == generation,
                    cancellationToken);
                if (tracked is null)
                {
                    rejected = 0;
                }
                else
                {
                    tracked.State = RuntimeState.Provisioning;
                    tracked.RunnerId = runnerId;
                    tracked.RunnerAssignmentReleaseToken = assignmentReleaseToken;
                    tracked.ProcessingVersion = nextVersion;
                    await db.SaveChangesAsync(cancellationToken);
                    rejected = 1;
                }
            }
            if (rejected == 0)
                return MessageExecutionOutcome.Superseded;
            var release = await capacity.ReleaseOrphanedAsync(
                runtimeInstanceId,
                cancellationToken);
            if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
                throw new InvalidOperationException(
                    "Mutable-image Runtime capacity changed owner during orphan recovery.");
            await outbox.PublishAsync(new RuntimeProvisionFailed(
                runtimeInstanceId,
                nextVersion,
                RuntimeFailureCode.InvalidConfiguration,
                runnerId));
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.Applied;
        }

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
            int updated;
            if (db.Database.IsRelational())
            {
                updated = await db.RuntimeInstances
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
            }
            else
            {
                var tracked = await db.RuntimeInstances.SingleOrDefaultAsync(
                    candidate => candidate.Id == runtimeInstanceId
                        && candidate.State == RuntimeState.Queued
                        && candidate.ProcessingVersion == processingVersion
                        && candidate.Generation == generation,
                    cancellationToken);
                if (tracked is null)
                {
                    updated = 0;
                }
                else
                {
                    tracked.State = RuntimeState.Provisioning;
                    tracked.RunnerId = runnerId;
                    tracked.ProcessingVersion = nextVersion;
                    await db.SaveChangesAsync(cancellationToken);
                    updated = 1;
                }
            }
            if (updated == 0)
            {
                var assignment = await db.RuntimeInstances.AsNoTracking()
                    .Where(candidate => candidate.Id == runtimeInstanceId)
                    .Select(candidate => new
                    {
                        candidate.RunnerId,
                        candidate.RunnerAssignmentReleaseToken
                    })
                    .SingleOrDefaultAsync(cancellationToken);
                var assignmentStillOwnsClaim = assignment is not null
                    && assignment.RunnerAssignmentReleaseToken is null
                    && string.Equals(
                        assignment.RunnerId,
                        runnerId,
                        StringComparison.Ordinal);
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
