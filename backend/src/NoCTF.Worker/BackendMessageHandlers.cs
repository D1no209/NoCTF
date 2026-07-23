using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycle;

namespace NoCTF.Worker;

public static class BackendMessageHandlers
{
    private static readonly TimeSpan RunnerReconciliationInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunnerDependencyRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompetitionLifecycleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AwdCheckerDispatchInterval = TimeSpan.FromSeconds(1);

    public static async Task Handle(
        DispatchAwdCheckers message,
        NoCtfDbContext db,
        AwdCheckerConfigurationCatalog configurations,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAwdCheckerDispatchAsync(
            message,
            db,
            configurations,
            outbox,
            cancellationToken);

    public static async Task<MessageExecutionOutcome> ExecuteAwdCheckerDispatchAsync(
        DispatchAwdCheckers message,
        NoCtfDbContext db,
        AwdCheckerConfigurationCatalog configurations,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var schedule = await db.DurableMaintenanceSchedules.SingleAsync(
            candidate => candidate.Kind == MaintenanceChainKind.AwdCheckerDispatch,
            cancellationToken);
        if (schedule.ProcessingVersion != message.ProcessingVersion)
            return MessageExecutionOutcome.Superseded;

        var targets = await db.RuntimeInstances
            .Where(runtime => runtime.State == RuntimeState.Running
                && runtime.NextCheckerDueAt != null
                && runtime.NextCheckerDueAt <= message.At
                && runtime.RunnerId != null
                && runtime.ControlCheckUrl != null
                && (message.AfterRuntimeInstanceId == null
                    || runtime.Id.CompareTo(message.AfterRuntimeInstanceId.Value) > 0))
            .Join(
                db.CompetitionChallenges,
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new { Runtime = runtime, Challenge = challenge })
            .Join(
                db.Competitions,
                pair => pair.Runtime.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new
                {
                    pair.Runtime,
                    pair.Challenge,
                    Competition = competition
                })
            .Where(target => target.Competition.Mode == NoCTF.Domain.Competitions.GameMode.Awd
                && target.Competition.Status == NoCTF.Domain.Competitions.CompetitionStatus.Running)
            .OrderBy(target => target.Runtime.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var applied = false;
        foreach (var target in targets)
        {
            var settings = configurations.Get(
                target.Competition.ConfigurationJson,
                target.Challenge.ConfigurationJson);
            if (settings.Checker is not { } checker)
            {
                target.Runtime.NextCheckerDueAt = null;
                target.Runtime.CheckerDeadlineAt = null;
                continue;
            }

            var interval = TimeSpan.FromSeconds(settings.CheckerIntervalSeconds);
            if (target.Runtime.CheckerSequence > target.Runtime.LastAppliedCheckerSequence)
            {
                if (target.Runtime.CheckerDeadlineAt is { } deadline && deadline <= message.At)
                {
                    target.Runtime.LastAppliedCheckerSequence = target.Runtime.CheckerSequence;
                    target.Runtime.LastAppliedCheckerBodySha256 = null;
                    target.Runtime.CheckerDeadlineAt = null;
                    await outbox.PublishAsync(new AwdCheckerFailed(
                        target.Runtime.CompetitionId,
                        target.Runtime.CompetitionChallengeId,
                        target.Runtime.Id,
                        target.Runtime.Generation,
                        target.Runtime.CheckerSequence,
                        target.Runtime.ProcessingVersion,
                        message.At));
                    applied = true;
                }
                target.Runtime.NextCheckerDueAt = message.At.Add(interval);
                continue;
            }

            target.Runtime.CheckerSequence = checked(target.Runtime.CheckerSequence + 1);
            target.Runtime.CheckerDeadlineAt = message.At.AddSeconds(checker.TimeoutSeconds);
            target.Runtime.NextCheckerDueAt = message.At.Add(interval);
            await outbox.PublishToRunnerNodeAsync(new RunAwdChecker(
                target.Runtime.Id,
                target.Runtime.CompetitionChallengeId,
                target.Runtime.Generation,
                target.Runtime.CheckerSequence,
                target.Runtime.ProcessingVersion,
                target.Runtime.CheckerDeadlineAt.Value,
                target.Runtime.RunnerPool,
                target.Runtime.RunnerId!));
            applied = true;
        }

        var pageIsFull = targets.Count == batchSize;
        var nextAt = pageIsFull ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.Add(AwdCheckerDispatchInterval);
        AdvanceMaintenanceSchedule(schedule, nextAt);
        await outbox.ScheduleAsync(new DispatchAwdCheckers(
            nextAt,
            schedule.ProcessingVersion,
            pageIsFull ? targets[^1].Runtime.Id : null), nextAt);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return applied ? MessageExecutionOutcome.Applied : MessageExecutionOutcome.Idempotent;
    }

    public static async Task Handle(
        AdvanceAwdRound message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.AdvanceAsync(message, cancellationToken);

    public static async Task Handle(
        GenerateAwdFlags message,
        IAwdRoundCoordinator coordinator,
        CancellationToken cancellationToken) =>
        _ = await coordinator.GenerateFlagsAsync(message, cancellationToken);

    public static async Task Handle(
        AwdFlagInjectionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == message.CompetitionId)
            .Select(candidate => new { candidate.OwnerId, candidate.ManagerIds })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return;
        var recipients = competition.ManagerIds.Append(competition.OwnerId).Distinct().ToArray();
        var existing = await db.Notifications.AsNoTracking()
            .Where(notification => recipients.Contains(notification.UserId)
                && notification.CompetitionId == message.CompetitionId
                && notification.EntityId == message.ChallengeFlagId
                && notification.Kind == NotificationKind.RuntimeStateChanged)
            .Select(notification => notification.UserId)
            .ToListAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            code = "awd_flag_injection_failed",
            message.CompetitionChallengeId,
            message.ChallengeFlagId,
            message.Generation,
            message.ProcessingVersion
        });
        foreach (var userId in recipients.Except(existing))
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(message.OccurredAt),
                UserId = userId,
                CompetitionId = message.CompetitionId,
                EntityId = message.ChallengeFlagId,
                Kind = NotificationKind.RuntimeStateChanged,
                PayloadJson = payload,
                CreatedAt = message.OccurredAt
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        AwdCheckerFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == message.CompetitionId)
            .Select(candidate => new { candidate.OwnerId, candidate.ManagerIds })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return;
        var recipients = competition.ManagerIds.Append(competition.OwnerId).Distinct().ToArray();
        var failureId = CreateAwdCheckerFailureId(
            message.RuntimeInstanceId,
            message.CheckerSequence);
        var existing = await db.Notifications.AsNoTracking()
            .Where(notification => recipients.Contains(notification.UserId)
                && notification.CompetitionId == message.CompetitionId
                && notification.EntityId == failureId
                && notification.Kind == NotificationKind.ManagementFailure)
            .Select(notification => notification.UserId)
            .ToListAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            code = "awd_checker_callback_missing",
            message.CompetitionChallengeId,
            message.RuntimeInstanceId,
            message.Generation,
            message.CheckerSequence,
            message.ProcessingVersion
        });
        foreach (var userId in recipients.Except(existing))
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(message.OccurredAt),
                UserId = userId,
                CompetitionId = message.CompetitionId,
                EntityId = failureId,
                Kind = NotificationKind.ManagementFailure,
                PayloadJson = payload,
                CreatedAt = message.OccurredAt
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid CreateAwdCheckerFailureId(Guid runtimeInstanceId, long checkerSequence)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], checkerSequence);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    public static async Task Handle(
        AdvanceCompetitionLifecycle message,
        CompetitionLifecycleAdvancer advancer,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteCompetitionLifecycleAsync(
            message,
            advancer,
            db,
            outbox,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteCompetitionLifecycleAsync(
        AdvanceCompetitionLifecycle message,
        CompetitionLifecycleAdvancer advancer,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var schedule = await db.DurableMaintenanceSchedules.SingleAsync(
            candidate => candidate.Kind == MaintenanceChainKind.CompetitionLifecycle,
            cancellationToken);
        if (schedule.ProcessingVersion != message.ProcessingVersion)
            return MessageExecutionOutcome.Superseded;
        var transitions = await advancer.ExecuteAsync(message.At, cancellationToken);
        var nextAt = DateTimeOffset.UtcNow.Add(CompetitionLifecycleInterval);
        AdvanceMaintenanceSchedule(schedule, nextAt);
        await outbox.ScheduleAsync(
            new AdvanceCompetitionLifecycle(nextAt, schedule.ProcessingVersion),
            nextAt);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return transitions.Count > 0
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    public static Task Handle(
        EvaluateSubmission message,
        ISubmissionProcessor processor,
        CancellationToken cancellationToken) =>
        processor.ProcessAsync(message.SubmissionId, message.ProcessingVersion, cancellationToken);

    public static Task Handle(
        ProjectLeaderboard message,
        ILeaderboardCache leaderboard,
        CancellationToken cancellationToken) =>
        leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);

    public static Task Handle(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken) =>
        results.RecordAwdpAsync(message, cancellationToken);

    public static async Task Handle(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var target = await db.RuntimeInstances
            .Join(
                db.CompetitionChallenges,
                instance => instance.CompetitionChallengeId,
                challenge => challenge.Id,
                (instance, challenge) => new { Instance = instance, Challenge = challenge })
            .Join(
                db.Competitions,
                pair => pair.Instance.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Instance, pair.Challenge, Competition = competition })
            .SingleOrDefaultAsync(item => item.Instance.Id == message.RuntimeInstanceId, cancellationToken);
        if (target is null ||
            target.Instance.State != RuntimeState.Queued ||
            target.Instance.ProcessingVersion != message.ProcessingVersion)
            return;

        var template = templates.Get(target.Competition.Mode, target.Challenge.ConfigurationJson);
        if (template is null || template.Provider == RuntimeProvider.Libvirt)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            target.Instance.ProcessingVersion = checked(target.Instance.ProcessingVersion + 1);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var limits = template.Limits ?? new ContainerResourceLimits(512 * 1024 * 1024, 500_000_000, 256);
        var definition = new ContainerRequest(
            target.Instance.Id,
            template.Provider,
            template.Image,
            template.Command ?? [],
            template.Environment ?? new Dictionary<string, string>(),
            MergeLabels(template.Labels, target.Instance),
            template.PortMappings ?? new Dictionary<int, int>(),
            limits,
            template.Security ?? new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            template.TtlSeconds is > 0 ? TimeSpan.FromSeconds(template.TtlSeconds.Value) : null,
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2));
        await outbox.PublishToRunnerPoolAsync(new ClaimContainerRuntime(
            target.Instance.Id,
            target.Instance.ProcessingVersion,
            target.Instance.Generation,
            target.Instance.RunnerPool,
            definition));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        StopRuntime message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null ||
            instance.State != RuntimeState.Stopping ||
            instance.ProcessingVersion != message.ProcessingVersion)
            return;
        if (string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            if (instance.RunnerAssignmentReleaseToken is not null)
                return;

            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = DateTimeOffset.UtcNow;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
                candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                    && candidate.State == RuntimeState.Queued,
                cancellationToken);
            if (replacement is not null)
                await outbox.PublishAsync(new DispatchRuntime(replacement.Id, replacement.ProcessingVersion));
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            instance.Id,
            instance.ProcessingVersion,
            instance.RunnerPool,
            instance.RunnerId
                ?? throw new InvalidOperationException("Runtime receipt has no owning Runner.")));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        CleanupCompetitionRuntimes message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var runtimes = await db.RuntimeInstances
            .Where(instance => instance.CompetitionId == message.CompetitionId
                && (instance.State == RuntimeState.Queued
                    || instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running))
            .OrderBy(instance => instance.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var instance in runtimes)
        {
            if (instance.State == RuntimeState.Queued && string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = now;
                instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
                continue;
            }

            instance.State = RuntimeState.Stopping;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            if (!string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
                await outbox.PublishAsync(new StopRuntime(instance.Id, instance.ProcessingVersion));
            else
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        ProvisionCompetitionRuntimes message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var queued = await db.RuntimeInstances
            .Where(instance => instance.CompetitionId == message.CompetitionId
                && instance.State == RuntimeState.Queued)
            .OrderBy(instance => instance.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var instance in queued)
            await outbox.PublishAsync(new DispatchRuntime(instance.Id, instance.ProcessingVersion));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteRunnerAssignmentReconciliationAsync(
            message,
            db,
            capacity,
            outbox,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteRunnerAssignmentReconciliationAsync(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var applied = false;
        var schedule = await db.DurableMaintenanceSchedules.SingleAsync(
            candidate => candidate.Kind == MaintenanceChainKind.RunnerAssignmentReconciliation,
            cancellationToken);
        if (schedule.ProcessingVersion != message.ProcessingVersion)
            return MessageExecutionOutcome.Superseded;

        var assignments = await db.RuntimeInstances
            .Where(instance => instance.RunnerId != null
                && (instance.State == RuntimeState.Provisioning
                    || instance.State == RuntimeState.Running
                    || instance.State == RuntimeState.Stopping)
                && (message.AfterRuntimeInstanceId == null
                    || instance.Id.CompareTo(message.AfterRuntimeInstanceId.Value) > 0))
            .OrderBy(instance => instance.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var heartbeatStatuses = new List<RunnerHeartbeatStatus>(assignments.Count);
        foreach (var instance in assignments)
        {
            var runnerId = instance.RunnerId!;
            var heartbeat = await capacity.GetHeartbeatAsync(
                instance.RunnerPool,
                runnerId,
                cancellationToken);
            heartbeatStatuses.Add(heartbeat);
            if (heartbeat == RunnerHeartbeatStatus.Unavailable)
            {
                var retryAt = DateTimeOffset.UtcNow.Add(RunnerDependencyRetryDelay);
                AdvanceMaintenanceSchedule(schedule, retryAt);
                await outbox.ScheduleAsync(
                    message with
                    {
                        At = retryAt,
                        ProcessingVersion = schedule.ProcessingVersion
                    },
                    retryAt);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                return MessageExecutionOutcome.DeferredCapacity;
            }
        }

        for (var index = 0; index < assignments.Count; index++)
        {
            var instance = assignments[index];
            var runnerId = instance.RunnerId!;
            var heartbeat = heartbeatStatuses[index];
            if (heartbeat == RunnerHeartbeatStatus.Online)
                continue;

            var hasReceipt = !string.IsNullOrWhiteSpace(instance.ProviderReceiptJson);
            var action = RunnerAssignmentRecoveryPolicy.Decide(
                instance.State,
                hasReceipt,
                instance.RunnerUnavailableAt is not null,
                instance.RunnerAssignmentReleaseToken is not null);
            if (action == RunnerAssignmentRecoveryAction.Ignore)
                continue;

            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            applied = true;
            switch (action)
            {
                case RunnerAssignmentRecoveryAction.Redispatch:
                    instance.RunnerAssignmentReleaseToken = Guid.CreateVersion7();
                    await outbox.PublishAsync(new ReleaseRunnerCapacity(
                        instance.Id,
                        instance.ProcessingVersion,
                        instance.RunnerPool,
                        runnerId,
                        instance.RunnerAssignmentReleaseToken.Value,
                        RunnerCapacityReleaseContinuation.DispatchRuntime));
                    break;
                case RunnerAssignmentRecoveryAction.CompleteStop:
                    instance.State = RuntimeState.Stopped;
                    instance.StoppedAt = message.At;
                    await outbox.PublishAsync(new ReleaseRunnerCapacity(
                        instance.Id,
                        instance.ProcessingVersion,
                        instance.RunnerPool,
                        runnerId,
                        Guid.CreateVersion7(),
                        RunnerCapacityReleaseContinuation.None));
                    break;
                case RunnerAssignmentRecoveryAction.AwaitOwnerCleanup:
                    instance.State = RuntimeState.Stopping;
                    instance.RunnerUnavailableAt = message.At;
                    await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
                        instance.Id,
                        instance.ProcessingVersion,
                        instance.RunnerPool,
                        runnerId));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        var pageIsFull = assignments.Count == 500;
        var nextAt = pageIsFull
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow.Add(RunnerReconciliationInterval);
        AdvanceMaintenanceSchedule(schedule, nextAt);
        await outbox.ScheduleAsync(
            new ReconcileRunnerAssignments(
                nextAt,
                schedule.ProcessingVersion,
                pageIsFull ? assignments[^1].Id : null),
            nextAt);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return applied
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    public static async Task Handle(
        ReleaseRunnerCapacity message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteRunnerCapacityReleaseAsync(
            message,
            db,
            capacity,
            outbox,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteRunnerCapacityReleaseAsync(
        ReleaseRunnerCapacity message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        var assignmentMatches = message.Continuation == RunnerCapacityReleaseContinuation.DispatchRuntime
            ? instance?.RunnerAssignmentReleaseToken == message.AssignmentReleaseToken
                && string.Equals(instance?.RunnerId, message.RunnerId, StringComparison.Ordinal)
            : string.Equals(instance?.RunnerId, message.RunnerId, StringComparison.Ordinal);
        if (instance is null
            || !assignmentMatches
            || !string.Equals(instance.RunnerPool, message.RunnerPool, StringComparison.Ordinal))
            return MessageExecutionOutcome.Superseded;

        var release = await capacity.ReleaseAsync(
            message.RuntimeInstanceId,
            message.RunnerId,
            cancellationToken);
        if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            return MessageExecutionOutcome.Conflict;

        if (message.Continuation == RunnerCapacityReleaseContinuation.DispatchRuntime)
        {
            instance.RunnerAssignmentReleaseToken = null;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            if (instance.State == RuntimeState.Provisioning
                && instance.ProcessingVersion == checked(message.ProcessingVersion + 1))
            {
                instance.State = RuntimeState.Queued;
                instance.RunnerId = null;
                await outbox.PublishAsync(new DispatchRuntime(
                    message.RuntimeInstanceId,
                    instance.ProcessingVersion));
            }
            else if (instance.State == RuntimeState.Stopping
                && string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
            {
                instance.State = RuntimeState.Stopped;
                instance.StoppedAt = DateTimeOffset.UtcNow;
                var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
                    candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                        && candidate.State == RuntimeState.Queued,
                    cancellationToken);
                if (replacement is not null)
                    await outbox.PublishAsync(new DispatchRuntime(
                        replacement.Id,
                        replacement.ProcessingVersion));
            }
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
        }

        return release == RunnerCapacityReleaseOutcome.Released
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    private static void AdvanceMaintenanceSchedule(
        DurableMaintenanceSchedule schedule,
        DateTimeOffset nextAt)
    {
        schedule.ProcessingVersion = checked(schedule.ProcessingVersion + 1);
        schedule.UpdatedAt = nextAt;
    }

    public static async Task Handle(
        DrainSubmissions message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        IQueryable<NoCTF.Domain.Submissions.Submission> query;
        if (message.SubmissionId is Guid submissionId)
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.id = {submissionId}
                  AND s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                FOR UPDATE SKIP LOCKED
                """);
        }
        else if (message.Rejudge)
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                  AND s.current_scoring_event_id IS NOT NULL
                  AND s.kind IN (0, 1)
                  AND s.evaluation_state <> 1
                  AND s.evaluation_state <> 2
                ORDER BY s.received_at, s.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """);
        }
        else
        {
            query = db.Submissions.FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM submissions AS s
                WHERE s.competition_id = {message.CompetitionId}
                  AND s.competition_challenge_id = {message.CompetitionChallengeId}
                  AND s.received_at <= {message.Cutoff}
                  AND (
                    s.evaluation_state = 0
                    OR (s.evaluation_state = 4 AND s.current_scoring_event_id IS NULL)
                  )
                ORDER BY s.received_at, s.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """);
        }

        var submissions = await query.ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var submission in submissions)
        {
            submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.Queued;
            submission.EvaluationFailureCode = null;
            submission.EvaluationUpdatedAt = now;
            submission.ProcessingVersion = checked(submission.ProcessingVersion + 1);
            await outbox.PublishAsync(new EvaluateSubmission(
                submission.Id,
                submission.ProcessingVersion));
        }
        if (submissions.Count == batchSize && message.SubmissionId is null)
            await outbox.PublishAsync(message);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static IReadOnlyDictionary<string, string> MergeLabels(
        IReadOnlyDictionary<string, string>? configured,
        RuntimeInstance instance)
    {
        var labels = configured is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(configured, StringComparer.Ordinal);
        labels["noctf.runtime-id"] = instance.Id.ToString("N");
        labels["noctf.competition-id"] = instance.CompetitionId.ToString("N");
        labels["noctf.competition-challenge-id"] = instance.CompetitionChallengeId.ToString("N");
        labels["noctf.generation"] = instance.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (instance.TeamId is Guid teamId)
            labels["noctf.team-id"] = teamId.ToString("N");
        return labels;
    }
}
