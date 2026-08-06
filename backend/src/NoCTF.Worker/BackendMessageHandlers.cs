using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Platform;
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
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Worker;

public static class BackendMessageHandlers
{
    private static readonly TimeSpan RunnerReconciliationInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunnerDependencyRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompetitionLifecycleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AwdCheckerDispatchInterval = TimeSpan.FromSeconds(1);

    public static Task Handle(
        CleanupObject message,
        IObjectStorage objects,
        CancellationToken cancellationToken) =>
        objects.DeleteAsync(message.ObjectKey, cancellationToken);

    public static async Task Handle(
        SendEmailVerification message,
        IEmailVerificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        var state = await delivery.SendVerificationAsync(
            message.UserId,
            message.Token,
            cancellationToken);
        if (state == EmailVerificationDeliveryState.NotConfigured)
        {
            throw new InvalidOperationException(
                "Email verification delivery was queued without a complete SMTP configuration.");
        }
    }

    public static async Task Handle(
        SendPasswordReset message,
        IPasswordResetEmailDelivery delivery,
        CancellationToken cancellationToken)
    {
        var state = await delivery.SendResetAsync(
            message.UserId,
            message.Token,
            cancellationToken);
        if (state == PasswordResetEmailDeliveryState.NotConfigured)
        {
            throw new InvalidOperationException(
                "Password reset delivery was queued without a complete SMTP configuration.");
        }
    }

    public static async Task Handle(
        SendPasswordChangedNotification message,
        IPasswordResetEmailDelivery delivery,
        CancellationToken cancellationToken) =>
        _ = await delivery.SendChangedNotificationAsync(
            message.UserId,
            cancellationToken);

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
                && ((runtime.NextCheckerDueAt != null
                        && runtime.NextCheckerDueAt <= message.At)
                    || (runtime.CheckerDeadlineAt != null
                        && runtime.CheckerDeadlineAt <= message.At))
                && runtime.RunnerId != null
                && runtime.AwdCheckerTargetHost != null
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
            .Join(
                db.Challenges,
                target => target.Challenge.ChallengeId,
                challenge => challenge.Id,
                (target, challenge) => new
                {
                    target.Runtime,
                    target.Challenge,
                    target.Competition,
                    Template = challenge
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
                target.Challenge.RulesJson,
                target.Template.DefinitionJson);
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
                    target.Runtime.CheckerStatus = AwdServiceState.Unknown;
                    target.Runtime.CheckerStatusUpdatedAt = message.At;
                    target.Runtime.CheckerDeadlineAt = null;
                    await outbox.PublishAsync(new AwdCheckerCallbackMissing(
                        target.Runtime.CompetitionId,
                        target.Runtime.CompetitionChallengeId,
                        target.Runtime.Id,
                        target.Runtime.Generation,
                        target.Runtime.CheckerSequence,
                        target.Runtime.ProcessingVersion,
                        message.At));
                    applied = true;
                }
                if (target.Runtime.NextCheckerDueAt <= message.At)
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
                target.Competition.ConfigurationRevision,
                target.Challenge.Revision,
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
                SourceEventKey = $"awd-flag-injection-failed:{message.ChallengeFlagId:N}:{message.ProcessingVersion}",
                PayloadJson = payload,
                CreatedAt = message.OccurredAt
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        AwdCheckerCallbackMissing message,
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
                SourceEventKey = $"awd-checker-callback-missing:{failureId:N}",
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
        ApplyCompetitionVisibility message,
        ApplyScheduledCompetitionVisibility visibility,
        CancellationToken cancellationToken) =>
        visibility.ExecuteAsync(
            message.CompetitionId,
            message.VisibilityRevision,
            DateTimeOffset.UtcNow,
            cancellationToken);

    public static async Task Handle(
        InvalidateLeaderboard message,
        ILeaderboardCache leaderboard,
        ILeaderboardSubscriptionRegistry subscriptions,
        CancellationToken cancellationToken)
    {
        await leaderboard.InvalidateAsync(
            message.CompetitionId,
            cancellationToken);
        if (await subscriptions.HasActiveAsync(
                message.CompetitionId,
                cancellationToken))
        {
            await leaderboard.RefreshAsync(
                message.CompetitionId,
                cancellationToken);
        }
    }

    public static Task Handle(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken) =>
        results.RecordAwdpAsync(message, cancellationToken);

    public static async Task Handle(
        ExpireAwdpFixVerification message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        if (DateTimeOffset.UtcNow < message.Deadline)
        {
            await outbox.ScheduleAsync(message, message.Deadline);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == message.RuntimeInstanceId,
            cancellationToken);
        var submission = await db.Submissions.SingleOrDefaultAsync(
            item => item.Id == message.SubmissionId,
            cancellationToken);
        if (runtime is null
            || submission is null
            || runtime.Purpose != RuntimePurpose.AwdpTarget
            || runtime.SubmissionId != submission.Id
            || runtime.Generation != message.Generation
            || runtime.ProcessingVersion != message.RuntimeProcessingVersion
            || runtime.State != RuntimeState.Running
            || !string.Equals(runtime.RunnerPool, message.RunnerPool, StringComparison.Ordinal)
            || !string.Equals(runtime.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || submission.ProcessingVersion != message.ProcessingVersion
            || submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing)
            return;

        submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.PlatformFailed;
        submission.EvaluationFailureCode = NoCTF.Domain.Submissions.ScoringFailureCode.CheckerPlatformError;
        submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
        runtime.State = RuntimeState.Stopping;
        runtime.RunnerAssignmentReleaseToken = null;
        runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.SubmissionEvaluated,
            CompetitionEventLevel.Error,
            CompetitionEventVisibility.Team,
            submission.EvaluationUpdatedAt,
            ActorUserId: submission.SubmittedByUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            SubmissionId: submission.Id,
            SubmissionKind: submission.Kind,
            SubmissionState: submission.EvaluationState,
            ScoringResult: NoCTF.Domain.Submissions.ScoringResult.PlatformFailed,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            runtime,
            CompetitionEventLevel.Warning,
            submission.EvaluationUpdatedAt,
            cancellationToken);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.ProcessingVersion,
            message.RunnerPool,
            message.RunnerId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
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
            .Join(
                db.Challenges,
                item => item.Challenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.Instance,
                    item.Challenge,
                    item.Competition,
                    Template = challenge
                })
            .SingleOrDefaultAsync(item => item.Instance.Id == message.RuntimeInstanceId, cancellationToken);
        if (target is null ||
            target.Instance.State != RuntimeState.Queued ||
            target.Instance.ProcessingVersion != message.ProcessingVersion)
            return;

        var awdpConfiguration = target.Instance.Purpose == RuntimePurpose.AwdpTarget
            ? AwdpConfigurationResolver.Resolve(
                target.Competition.ConfigurationJson,
                target.Challenge.RulesJson,
                target.Template.DefinitionJson)
            : null;
        var template = awdpConfiguration?.Runtime
            ?? templates.Get(target.Competition.Mode, target.Template.DefinitionJson);
        if (template is null)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            target.Instance.ProcessingVersion = checked(target.Instance.ProcessingVersion + 1);
            await FailAwdpSubmissionAsync(target.Instance, db, cancellationToken);
            await RecordRuntimeStateAsync(
                events,
                target.Instance,
                CompetitionEventLevel.Error,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        string? perTeamFlag = null;
        if (target.Competition.Mode == GameMode.Ctf
            && template.FlagSource == RuntimeFlagSource.PerTeam)
        {
            if (target.Instance.TeamId is Guid teamId)
            {
                perTeamFlag = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.CompetitionChallengeId == target.Challenge.Id
                        && flag.TeamId == teamId
                        && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                        && flag.SpecificationId == target.Challenge.Id)
                    .Select(flag => flag.Flag)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            if (perTeamFlag is null)
            {
                target.Instance.State = RuntimeState.Failed;
                target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
                target.Instance.ProcessingVersion =
                    checked(target.Instance.ProcessingVersion + 1);
                await RecordRuntimeStateAsync(
                    events,
                    target.Instance,
                    CompetitionEventLevel.Error,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                return;
            }
        }

        IRunnerPoolMessage claim;
        try
        {
            if (target.Instance.Purpose == RuntimePurpose.AwdpTarget)
            {
                var definition = AwdpTargetDefinitionFactory.Create(
                    target.Instance.Id,
                    target.Instance.Generation,
                    template,
                    target.Instance.RuntimeProvider,
                    DateTimeOffset.UtcNow);
                if (target.Instance.RuntimeProvider == RuntimeProvider.Docker)
                {
                    definition = definition with
                    {
                        PortMappings = definition.PortMappings.Keys.ToDictionary(
                            port => port,
                            _ => 0)
                    };
                }
                claim = new ClaimContainerRuntime(
                    target.Instance.Id,
                    target.Instance.ProcessingVersion,
                    target.Instance.Generation,
                    target.Instance.RunnerPool,
                    definition);
            }
            else
            {
                claim = RuntimeClaimFactory.Create(
                    target.Instance,
                    target.Competition.Mode,
                    template,
                    target.Template.DefinitionJson,
                    perTeamFlag);
            }
        }
        catch (InvalidOperationException)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            target.Instance.ProcessingVersion = checked(target.Instance.ProcessingVersion + 1);
            await FailAwdpSubmissionAsync(target.Instance, db, cancellationToken);
            await RecordRuntimeStateAsync(
                events,
                target.Instance,
                CompetitionEventLevel.Error,
                DateTimeOffset.UtcNow,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        await PublishRuntimeClaimAsync(outbox, claim);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async Task FailAwdpSubmissionAsync(
        RuntimeInstance instance,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose != RuntimePurpose.AwdpTarget
            || instance.SubmissionId is not Guid submissionId)
            return;
        var submission = await db.Submissions.SingleOrDefaultAsync(
            item => item.Id == submissionId,
            cancellationToken);
        if (submission is null
            || submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing
            || submission.ProcessingVersion != instance.SubmissionProcessingVersion)
            return;
        submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.PlatformFailed;
        submission.EvaluationFailureCode = NoCTF.Domain.Submissions.ScoringFailureCode.CheckerPlatformError;
        submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
    }

    public static async Task Handle(
        StopRuntime message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null ||
            instance.State != RuntimeState.Stopping ||
            instance.ProcessingVersion != message.ProcessingVersion)
            return;
        if (string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            if (instance.RunnerId is not null
                || instance.RunnerAssignmentReleaseToken is not null)
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

    public static async Task Handle(
        CleanupCompetitionRuntimes message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
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
                await RecordRuntimeStateAsync(
                    events,
                    instance,
                    CompetitionEventLevel.Information,
                    now,
                    cancellationToken);
                continue;
            }

            instance.State = RuntimeState.Stopping;
            instance.RunnerAssignmentReleaseToken = null;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            await outbox.PublishAsync(new StopRuntime(instance.Id, instance.ProcessingVersion));
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

    public static async Task Handle(
        ProvisionCompetitionRuntimes message,
        IAwdRuntimeProvisioner awdRuntimes,
        IKohRuntimeProvisioner kohRuntimes,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var awdOutcome = await awdRuntimes.EnsureAsync(
            message.CompetitionId,
            cancellationToken);
        if (awdOutcome == AwdRuntimeProvisioningOutcome.DeferredCleanup)
        {
            var retryAt = DateTimeOffset.UtcNow.Add(RunnerDependencyRetryDelay);
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
            var retryAt = DateTimeOffset.UtcNow.Add(RunnerDependencyRetryDelay);
            await outbox.ScheduleAsync(message, retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        if (kohOutcome != KohRuntimeProvisioningOutcome.NotApplicable)
            return;
        var queued = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == message.CompetitionId
                && instance.State == RuntimeState.Queued
                && (instance.ReplacesRuntimeInstanceId == null
                    || db.RuntimeInstances.Any(predecessor =>
                        predecessor.Id == instance.ReplacesRuntimeInstanceId
                        && predecessor.State == RuntimeState.Stopped)))
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
            instance.RunnerAssignmentReleaseToken = null;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            if (!string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
                await outbox.PublishAsync(new StopRuntime(instance.Id, instance.ProcessingVersion));
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

        var pageIsFull = assignments.Count == 500;
        var resourceAudits = new List<ReconcileRuntimeResources>();
        if (!pageIsFull)
        {
            var failedCleanupOwners = await db.RuntimeInstances.AsNoTracking()
                .Where(instance => instance.State == RuntimeState.Failed
                    && instance.ProviderReceiptJson != null
                    && instance.RunnerId != null)
                .Select(instance => new { instance.RunnerPool, instance.RunnerId })
                .Distinct()
                .ToListAsync(cancellationToken);
            var failedCleanupOwnerSet = failedCleanupOwners
                .Select(owner => (owner.RunnerPool, RunnerId: owner.RunnerId!))
                .ToHashSet();
            var runtimePools = await db.RuntimeInstances.AsNoTracking()
                .Select(instance => instance.RunnerPool)
                .Distinct()
                .OrderBy(pool => pool)
                .ToListAsync(cancellationToken);
            foreach (var pool in runtimePools)
            {
                var inventory = await capacity.GetPoolInventoryAsync(
                    pool,
                    cancellationToken);
                if (inventory.Availability == RunnerPoolInventoryAvailability.Unavailable)
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

                foreach (var inventoryRunnerId in inventory.RunnerIds)
                {
                    var heartbeat = await capacity.GetHeartbeatAsync(
                        pool,
                        inventoryRunnerId,
                        cancellationToken);
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
                    if (heartbeat == RunnerHeartbeatStatus.Online
                        || failedCleanupOwnerSet.Contains((pool, inventoryRunnerId)))
                    {
                        resourceAudits.Add(new(
                            pool,
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
            var assignmentReleasePending = instance.RunnerAssignmentReleaseToken is not null;
            var hasReceipt = !string.IsNullOrWhiteSpace(instance.ProviderReceiptJson);
            if (!hasReceipt && assignmentReleasePending)
            {
                instance.RunnerAssignmentReleaseToken = null;
                applied = true;
                continue;
            }
            if (heartbeat == RunnerHeartbeatStatus.Online && !assignmentReleasePending)
                continue;

            var action = RunnerAssignmentRecoveryPolicy.Decide(
                instance.State,
                hasReceipt,
                instance.RunnerUnavailableAt is not null,
                assignmentReleasePending);
            if (action == RunnerAssignmentRecoveryAction.Ignore)
                continue;

            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            applied = true;
            switch (action)
            {
                case RunnerAssignmentRecoveryAction.AwaitOwnerCleanup:
                    instance.State = RuntimeState.Stopping;
                    instance.RunnerAssignmentReleaseToken = null;
                    instance.RunnerUnavailableAt = message.At;
                    await PublishRuntimeStopAsync(outbox, instance, runnerId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        foreach (var audit in resourceAudits)
            await outbox.PublishToRunnerNodeAsync(audit);
        applied |= resourceAudits.Count > 0;
        var nextAt = pageIsFull || expiredRuntimes.Count == 500
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
        var outcome = await ExecuteRunnerCapacityReleaseAsync(
            message,
            db,
            capacity,
            outbox,
            cancellationToken);
        if (outcome == MessageExecutionOutcome.Conflict)
            throw new InvalidOperationException(
                $"Runner capacity assignment for Runtime '{message.RuntimeInstanceId}' is owned by another Runner.");
    }

    public static Task<MessageExecutionOutcome> ExecuteRunnerCapacityReleaseAsync(
        ReleaseRunnerCapacity message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        // Tombstone: safely consume already-persisted legacy messages without releasing or dispatching.
        return Task.FromResult(MessageExecutionOutcome.Superseded);
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
        var candidates = db.Submissions.Where(submission =>
            submission.CompetitionId == message.CompetitionId
            && submission.CompetitionChallengeId == message.CompetitionChallengeId
            && submission.ReceivedAt <= message.Cutoff);
        if (message.SubmissionId is Guid submissionId)
        {
            candidates = candidates.Where(submission =>
                submission.Id == submissionId
                && submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Queued
                && submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing);
        }
        else if (message.Rejudge)
        {
            candidates = candidates.Where(submission =>
                submission.CurrentScoringEventId != null
                && (submission.Kind == NoCTF.Domain.Submissions.SubmissionKind.Flag
                    || submission.Kind == NoCTF.Domain.Submissions.SubmissionKind.Break)
                && submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Queued
                && submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing);
        }
        else
        {
            candidates = candidates.Where(submission =>
                submission.EvaluationState == NoCTF.Domain.Submissions.SubmissionEvaluationState.Pending
                || (submission.EvaluationState == NoCTF.Domain.Submissions.SubmissionEvaluationState.PlatformFailed
                    && submission.CurrentScoringEventId == null));
        }

        var candidateIds = await candidates.AsNoTracking()
            .OrderBy(submission => submission.ReceivedAt)
            .ThenBy(submission => submission.Id)
            .Take(message.SubmissionId is null ? batchSize : 1)
            .Select(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0)
            return;

        var claimId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await candidates.Where(submission => candidateIds.Contains(submission.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    submission => submission.EvaluationState,
                    NoCTF.Domain.Submissions.SubmissionEvaluationState.Queued)
                .SetProperty(submission => submission.EvaluationFailureCode,
                    (NoCTF.Domain.Submissions.ScoringFailureCode?)null)
                .SetProperty(submission => submission.EvaluationResultBodySha256,
                    (byte[]?)null)
                .SetProperty(submission => submission.EvaluationUpdatedAt, now)
                .SetProperty(
                    submission => submission.ProcessingVersion,
                    submission => submission.ProcessingVersion + 1)
                .SetProperty(submission => submission.EvaluationClaimId, claimId),
                cancellationToken);
        var submissions = await db.Submissions.AsNoTracking()
            .Where(submission => submission.EvaluationClaimId == claimId)
            .OrderBy(submission => submission.ReceivedAt)
            .ThenBy(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var submission in submissions)
        {
            await outbox.PublishAsync(new EvaluateSubmission(
                submission.Id,
                submission.ProcessingVersion));
        }
        if (candidateIds.Length == batchSize && message.SubmissionId is null)
            await outbox.PublishAsync(message);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static ValueTask PublishRuntimeClaimAsync(
        ITransactionalMessageOutbox outbox,
        IRunnerPoolMessage claim) =>
        claim switch
        {
            ClaimContainerRuntime message => outbox.PublishToRunnerPoolAsync(message),
            ClaimComposeRuntime message => outbox.PublishToRunnerPoolAsync(message),
            ClaimOvaRuntime message => outbox.PublishToRunnerPoolAsync(message),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime claim type '{claim.GetType().Name}'.")
        };

    private static ValueTask<Guid> RecordRuntimeStateAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance instance,
        CompetitionEventLevel level,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            instance.CompetitionId,
            CompetitionEventKind.RuntimeStateChanged,
            level,
            instance.TeamId is null
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: instance.TeamId,
            CompetitionChallengeId: instance.CompetitionChallengeId,
            RuntimeInstanceId: instance.Id,
            SubmissionId: instance.SubmissionId,
            RuntimeState: instance.State,
            RuntimeGeneration: instance.Generation),
            cancellationToken);

    private static ValueTask PublishRuntimeStopAsync(
        ITransactionalMessageOutbox outbox,
        RuntimeInstance instance,
        string runnerId) =>
        instance.RuntimeKind switch
        {
            RuntimeKind.Container => outbox.PublishToRunnerNodeAsync(
                new StopContainerRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            RuntimeKind.Compose => outbox.PublishToRunnerNodeAsync(
                new StopComposeRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            RuntimeKind.OvaVm => outbox.PublishToRunnerNodeAsync(
                new StopOvaRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime kind '{instance.RuntimeKind}'.")
        };
}
