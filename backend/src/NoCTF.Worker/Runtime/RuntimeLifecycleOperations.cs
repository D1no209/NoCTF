using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Platform;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static async Task StopRuntimeAsync(
        StopRuntime message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null || instance.State != RuntimeState.Stopping)
            return;
        await FailAwdpSubmissionAsync(
            instance,
            db,
            outbox,
            events,
            timeProvider,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            if (instance.RunnerId is { } runnerId)
            {
                await PublishRuntimeStopAsync(outbox, instance, runnerId);
                await outbox.FlushOutgoingMessagesAsync();
                return;
            }

            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = timeProvider.GetUtcNow();
            await RecordRuntimeStateAsync(
                events,
                instance,
                CompetitionEventLevel.Information,
                instance.StoppedAt.Value,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        await PublishRuntimeStopAsync(
            outbox,
            instance,
            instance.RunnerId
                ?? throw new InvalidOperationException("Runtime receipt has no owning Runner."));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task CleanupCompetitionRuntimesAsync(
        CleanupCompetitionRuntimes message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var runtimes = await db.RuntimeInstances
            .Where(instance => instance.CompetitionId == message.CompetitionId
                && (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || (instance.Purpose == RuntimePurpose.AwdpTarget
                        && instance.GameplayFactId != null)))
            .OrderBy(instance => instance.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var instance in runtimes)
        {
            await FailAwdpSubmissionAsync(
                instance,
                db,
                outbox,
                events,
                timeProvider,
                cancellationToken);
            if (instance.State == RuntimeState.Queued && string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = now;
                await RecordRuntimeStateAsync(
                    events,
                    instance,
                    CompetitionEventLevel.Information,
                    now,
                    cancellationToken);
                continue;
            }

            if (instance.State is RuntimeState.Stopped or RuntimeState.Failed)
                continue;

            if (instance.State == RuntimeState.Stopping)
            {
                await outbox.PublishAsync(new StopRuntime(instance.Id));
                continue;
            }

            instance.State = RuntimeState.Stopping;
            await outbox.PublishAsync(new StopRuntime(instance.Id));
            await RecordRuntimeStateAsync(
                events,
                instance,
                CompetitionEventLevel.Information,
                now,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ProvisionCompetitionRuntimesAsync(
        ProvisionCompetitionRuntimes message,
        IAwdRuntimeProvisioner awdRuntimes,
        IKohRuntimeProvisioner kohRuntimes,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var awdOutcome = await awdRuntimes.EnsureAsync(
            message.CompetitionId,
            cancellationToken);
        if (awdOutcome == AwdRuntimeProvisioningOutcome.DeferredCleanup)
        {
            var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
            await outbox.ScheduleAsync(message, retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        if (awdOutcome != AwdRuntimeProvisioningOutcome.NotApplicable)
            return;
        var kohOutcome = await kohRuntimes.EnsureAsync(
            message.CompetitionId,
            cancellationToken);
        if (kohOutcome == KohRuntimeProvisioningOutcome.DeferredCleanup)
        {
            var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
            await outbox.ScheduleAsync(message, retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        if (kohOutcome != KohRuntimeProvisioningOutcome.NotApplicable)
            return;
        var queued = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == message.CompetitionId
                && instance.State == RuntimeState.Queued)
            .OrderBy(instance => instance.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var instance in queued)
            await outbox.PublishAsync(new DispatchRuntime(instance.Id));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task ReconcileRunnerAssignmentsAsync(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteRunnerAssignmentReconciliationAsync(
            message,
            db,
            capacity,
            placementPolicy,
            outbox,
            timeProvider,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteRunnerAssignmentReconciliationAsync(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var applied = false;
        var solvedRuntimes = await db.RuntimeInstances
            .Where(instance =>
                (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running)
                && (instance.RuntimeKind == RuntimeKind.Container
                    || instance.RuntimeKind == RuntimeKind.Compose)
                && (instance.Purpose == RuntimePurpose.Player
                    && db.GameplayFacts.Any(fact =>
                        fact.CompetitionId == instance.CompetitionId
                        && fact.CompetitionChallengeId == instance.CompetitionChallengeId
                        && fact.TeamId == instance.TeamId
                        && fact.Kind == GameplayFactKind.FlagAttempt
                        && fact.State == GameplayFactState.Completed
                        && fact.Result == GameplayFactResult.Correct)
                    || instance.Purpose == RuntimePurpose.AwdpAttack
                    && db.GameplayFacts.Any(fact =>
                        fact.CompetitionId == instance.CompetitionId
                        && fact.CompetitionChallengeId == instance.CompetitionChallengeId
                        && fact.TeamId == instance.TeamId
                        && fact.Kind == GameplayFactKind.BreakAttempt
                        && fact.State == GameplayFactState.Completed
                        && fact.Result == GameplayFactResult.Correct)))
            .OrderBy(instance => instance.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var instance in solvedRuntimes)
        {
            if (instance.State == RuntimeState.Queued
                && string.IsNullOrWhiteSpace(instance.ProviderReceiptJson)
                && instance.RunnerId is null)
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = message.At;
            }
            else
            {
                instance.State = RuntimeState.Stopping;
                await outbox.PublishAsync(new StopRuntime(
                    instance.Id));
            }
        }
        applied |= solvedRuntimes.Count > 0;

        var expiredRuntimes = await db.RuntimeInstances
            .Where(instance => instance.State == RuntimeState.Running
                && instance.ExpiresAt != null
                && instance.ExpiresAt <= message.At)
            .OrderBy(instance => instance.ExpiresAt)
            .ThenBy(instance => instance.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var instance in expiredRuntimes)
        {
            instance.State = RuntimeState.Stopping;
            if (!string.IsNullOrWhiteSpace(instance.ProviderReceiptJson)
                || instance.RunnerId is not null)
                await outbox.PublishAsync(new StopRuntime(instance.Id));
            else
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = message.At;
            }
        }
        applied |= expiredRuntimes.Count > 0;

        var assignments = await db.RuntimeInstances
            .Where(instance => instance.RunnerId != null
                && (instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || instance.State == RuntimeState.Stopping
                    || (instance.State == RuntimeState.Failed
                        && instance.ProviderReceiptJson == null))
                && (message.AfterRuntimeInstanceId == null
                    || instance.Id.CompareTo(message.AfterRuntimeInstanceId.Value) > 0))
            .OrderBy(instance => instance.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var heartbeatStatuses = new List<RunnerHeartbeatStatus>(assignments.Count);
        foreach (var instance in assignments)
        {
            var runnerId = instance.RunnerId!;
            var pool = placementPolicy.Resolve(instance.RuntimeKind).RunnerPool;
            var heartbeat = await capacity.GetHeartbeatAsync(
                pool,
                runnerId,
                cancellationToken);
            heartbeatStatuses.Add(heartbeat);
            if (heartbeat == RunnerHeartbeatStatus.Unavailable)
            {
                var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
                await outbox.ScheduleAsync(
                    message with { At = retryAt },
                    retryAt);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                return MessageExecutionOutcome.DeferredCapacity;
            }
        }

        var pageIsFull = assignments.Count == 500;
        var resourceAudits = new List<ReconcileRuntimeResources>();
        if (!pageIsFull)
        {
            var failedCleanupOwners = await db.RuntimeInstances.AsNoTracking()
                .Where(instance => instance.State == RuntimeState.Failed
                    && instance.RunnerId != null)
                .Select(instance => instance.RunnerId!)
                .Distinct()
                .ToListAsync(cancellationToken);
            var failedCleanupOwnerSet = failedCleanupOwners.ToHashSet(StringComparer.Ordinal);
            // A deployment owns one active container provider/pool. Do not enumerate
            // every domain RuntimeKind here: unsupported kinds (for example OVA on a
            // Docker deployment) are intentionally rejected by the placement policy
            // and must not abort reconciliation for the configured pool.
            var runtimePools = new[]
            {
                placementPolicy.Resolve(RuntimeKind.Container).RunnerPool
            };
            foreach (var pool in runtimePools)
            {
                var inventory = await capacity.GetPoolInventoryAsync(
                    pool,
                    cancellationToken);
                if (inventory.Availability == RunnerPoolInventoryAvailability.Unavailable)
                {
                    var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
                    await outbox.ScheduleAsync(
                        message with { At = retryAt },
                        retryAt);
                    await db.SaveChangesAsync(cancellationToken);
                    await outbox.FlushOutgoingMessagesAsync();
                    return MessageExecutionOutcome.DeferredCapacity;
                }

                foreach (var inventoryRunnerId in inventory.RunnerIds)
                {
                    var heartbeat = await capacity.GetHeartbeatAsync(
                        pool,
                        inventoryRunnerId,
                        cancellationToken);
                    if (heartbeat == RunnerHeartbeatStatus.Unavailable)
                    {
                        var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
                        await outbox.ScheduleAsync(
                            message with { At = retryAt },
                            retryAt);
                        await db.SaveChangesAsync(cancellationToken);
                        await outbox.FlushOutgoingMessagesAsync();
                        return MessageExecutionOutcome.DeferredCapacity;
                    }
                    if (heartbeat == RunnerHeartbeatStatus.Online
                        || failedCleanupOwnerSet.Contains(inventoryRunnerId))
                    {
                        resourceAudits.Add(new(
                            inventoryRunnerId,
                            message.At));
                    }
                }
            }
        }

        for (var index = 0; index < assignments.Count; index++)
        {
            var instance = assignments[index];
            var runnerId = instance.RunnerId!;
            var heartbeat = heartbeatStatuses[index];
            if (heartbeat == RunnerHeartbeatStatus.Online)
                continue;

            if (instance.State is RuntimeState.Stopping or RuntimeState.Failed)
            {
                instance.State = RuntimeState.Stopping;
                await PublishRuntimeStopAsync(outbox, instance, runnerId);
                applied = true;
            }
        }

        foreach (var audit in resourceAudits)
            await outbox.PublishToRunnerNodeAsync(audit);
        applied |= resourceAudits.Count > 0;
        if (pageIsFull || expiredRuntimes.Count == 500 || solvedRuntimes.Count == 500)
            await outbox.PublishAsync(new ReconcileRunnerAssignments(
                message.At,
                pageIsFull ? assignments[^1].Id : null));
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return applied
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    private static ValueTask PublishRuntimeProvisionAsync(
        ITransactionalMessageOutbox outbox,
        IRuntimeProvisionMessage provision) =>
        provision switch
        {
            ProvisionContainerRuntime message => outbox.PublishToRunnerNodeAsync(message),
            ProvisionComposeRuntime message => outbox.PublishToRunnerNodeAsync(message),
            ProvisionOvaRuntime message => outbox.PublishToRunnerNodeAsync(message),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime provision type '{provision.GetType().Name}'.")
        };

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

    private static ValueTask PublishRuntimeStopAsync(
        ITransactionalMessageOutbox outbox,
        RuntimeInstance instance,
        string runnerId) =>
        instance.RuntimeKind switch
        {
            RuntimeKind.Container => outbox.PublishToRunnerNodeAsync(
                new StopContainerRuntime(
                    instance.Id,
                    runnerId)),
            RuntimeKind.Compose => outbox.PublishToRunnerNodeAsync(
                new StopComposeRuntime(
                    instance.Id,
                    runnerId)),
            RuntimeKind.OvaVm => outbox.PublishToRunnerNodeAsync(
                new StopOvaRuntime(
                    instance.Id,
                    runnerId)),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime kind '{instance.RuntimeKind}'.")
        };

}
