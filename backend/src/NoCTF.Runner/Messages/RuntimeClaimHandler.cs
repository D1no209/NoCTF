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

    public async Task<MessageExecutionOutcome> ExecuteAsync(
        ClaimContainerRuntime message,
        CancellationToken cancellationToken)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        if (!string.Equals(message.RunnerPool, configuredPool, StringComparison.Ordinal)
            || RunnerQueueName.FromPool(message.RunnerPool) != RunnerQueueName.FromPool(configuredPool))
        {
            throw new InvalidOperationException(
                "Runtime claim was delivered to the wrong Runner pool.");
        }

        var runnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        var instance = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.State != RuntimeState.Queued
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.Generation != message.Generation)
            return instance is not null
                && instance.State == RuntimeState.Provisioning
                && instance.Generation == message.Generation
                && instance.ProcessingVersion == checked(message.ProcessingVersion + 1)
                && string.Equals(instance.RunnerId, runnerId, StringComparison.Ordinal)
                ? MessageExecutionOutcome.Idempotent
                : MessageExecutionOutcome.Superseded;

        var limits = message.Definition.Limits;
        var claim = await capacity.TryClaimForRunnerAsync(
            new RunnerCapacityRequest(
                message.RuntimeInstanceId,
                message.RunnerPool,
                limits.MemoryBytes,
                limits.NanoCpus,
                limits.PidsLimit),
            runnerId,
            cancellationToken);
        if (claim.Availability != RunnerCapacityAvailability.Claimed)
        {
            await outbox.ScheduleToRunnerPoolAsync(
                message,
                DateTimeOffset.UtcNow.Add(CapacityRetryDelay));
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.DeferredCapacity;
        }

        try
        {
            var nextVersion = checked(message.ProcessingVersion + 1);
            var updated = await db.RuntimeInstances
                .Where(candidate => candidate.Id == message.RuntimeInstanceId
                    && candidate.State == RuntimeState.Queued
                    && candidate.ProcessingVersion == message.ProcessingVersion
                    && candidate.Generation == message.Generation)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.State, RuntimeState.Provisioning)
                        .SetProperty(candidate => candidate.RunnerId, runnerId)
                        .SetProperty(candidate => candidate.ProcessingVersion, nextVersion),
                    cancellationToken);
            if (updated == 0)
            {
                var assignmentStillOwnsClaim = await db.RuntimeInstances.AsNoTracking().AnyAsync(
                    candidate => candidate.Id == message.RuntimeInstanceId
                        && candidate.RunnerId == runnerId,
                    cancellationToken);
                if (claim.State == RunnerCapacityClaimState.Acquired && !assignmentStillOwnsClaim)
                    await capacity.ReleaseAsync(message.RuntimeInstanceId, runnerId, cancellationToken);
                return MessageExecutionOutcome.Superseded;
            }

            await outbox.PublishToRunnerNodeAsync(new ProvisionContainerRuntime(
                message.RuntimeInstanceId,
                nextVersion,
                message.Generation,
                message.RunnerPool,
                runnerId,
                message.Definition));
            await outbox.FlushOutgoingMessagesAsync();
            return MessageExecutionOutcome.Applied;
        }
        catch
        {
            await capacity.ReleaseAsync(message.RuntimeInstanceId, runnerId, cancellationToken);
            throw;
        }
    }
}
