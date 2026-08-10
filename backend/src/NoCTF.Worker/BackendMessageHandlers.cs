using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
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
using NoCTF.Domain.Shared;

namespace NoCTF.Worker;

public static class BackendMessageHandlers
{
    private static readonly TimeSpan RunnerDependencyRetryDelay = TimeSpan.FromSeconds(5);

    public static async Task Handle(
        CleanupFile message,
        NoCtfDbContext db,
        IObjectStorage objects,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM files WHERE id = {message.FileId} FOR UPDATE",
            cancellationToken);
        var file = await db.Files.SingleOrDefaultAsync(item => item.Id == message.FileId, cancellationToken);
        if (file is null)
            return;
        var referenced = await db.Users.AnyAsync(item => item.AvatarFileId == file.Id, cancellationToken)
            || await db.Teams.AnyAsync(item => item.AvatarFileId == file.Id, cancellationToken)
            || await db.Competitions.AnyAsync(item => item.PosterFileId == file.Id, cancellationToken)
            || await db.PlatformSettings.AnyAsync(item => item.LogoFileId == file.Id, cancellationToken)
            || await db.Set<ChallengeAttachment>().AnyAsync(item => item.FileId == file.Id, cancellationToken)
            || await db.PatchUploads.AnyAsync(item => item.FileId == file.Id, cancellationToken)
            || await db.DataExports.AnyAsync(item => item.FileId == file.Id, cancellationToken);
        if (referenced)
            return;
        await objects.DeleteAsync(file.ObjectKey, cancellationToken);
        db.Files.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

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
        if (pageIsFull)
            await outbox.PublishAsync(new DispatchAwdCheckers(message.At, targets[^1].Runtime.Id));
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
        var existing = await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == message.CompetitionId
                && notification.RelatedType == EntityReferenceKind.CompetitionChallenge
                && notification.RelatedId == message.CompetitionChallengeId
                && notification.Kind == NotificationKind.RuntimeStateChanged)
            ;
        if (existing)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            code = "awd_flag_injection_failed",
            message.CompetitionChallengeId,
            message.ChallengeFlagId,
            message.Generation,
            message.ProcessingVersion
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(message.OccurredAt),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = message.CompetitionId,
            Kind = NotificationKind.RuntimeStateChanged,
            ContentJson = payload,
            RelatedType = EntityReferenceKind.CompetitionChallenge,
            RelatedId = message.CompetitionChallengeId,
            SentAt = message.OccurredAt
        });
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
        var existing = await db.Notifications.AsNoTracking()
            .AnyAsync(notification =>
                notification.TargetType == NotificationTargetType.CompetitionCollaborators
                && notification.TargetId == message.CompetitionId
                && notification.RelatedType == EntityReferenceKind.RuntimeInstance
                && notification.RelatedId == message.RuntimeInstanceId
                && notification.Kind == NotificationKind.ManagementFailure)
            ;
        if (existing)
            return;
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            code = "awd_checker_callback_missing",
            message.CompetitionChallengeId,
            message.RuntimeInstanceId,
            message.Generation,
            message.CheckerSequence,
            message.ProcessingVersion
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(message.OccurredAt),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = message.CompetitionId,
            Kind = NotificationKind.ManagementFailure,
            ContentJson = payload,
            RelatedType = EntityReferenceKind.RuntimeInstance,
            RelatedId = message.RuntimeInstanceId,
            SentAt = message.OccurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
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
        var transitions = await advancer.ExecuteAsync(message.At, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return transitions.Count > 0
            ? MessageExecutionOutcome.Applied
            : MessageExecutionOutcome.Idempotent;
    }

    public static Task Handle(
        EvaluateGameplayFact message,
        IGameplayFactProcessor processor,
        CancellationToken cancellationToken) =>
        processor.ProcessAsync(message.GameplayFactId, cancellationToken);

    public static async Task Handle(
        GameplayFactStateChanged message,
        NoCtfDbContext db,
        NoCTF.Application.Notifications.IGameplayFactStateChangedNotification notifications,
        CancellationToken cancellationToken)
    {
        var fact = await db.GameplayFacts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == message.GameplayFactId, cancellationToken);
        if (fact?.ActorUserId is not Guid userId || fact.State != message.State)
            return;
        var view = new NoCTF.Application.GameplayFacts.Status.GameplayFactStatusView(
            fact.Id, fact.CompetitionId, fact.TeamId, fact.CompetitionChallengeId, fact.Kind,
            fact.State,
            NoCTF.Application.GameplayFacts.Status.GameplayFactResultDisclosure.PlayerResult(fact.Result, fact.FailureCode),
            NoCTF.Application.GameplayFacts.Status.GameplayFactResultDisclosure.PlayerFailureCode(fact.FailureCode),
            fact.OccurredAt, fact.UpdatedAt);
        await notifications.PublishAsync(
            new NoCTF.Application.Notifications.GameplayFactStateChangedNotification(userId, view),
            cancellationToken);
    }

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
        RefreshDirtyLeaderboards message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        _ = message;
        if (db.Database.IsInMemory())
        {
            while (true)
            {
                var developmentCompetitions = await db.Competitions
                    .Where(competition => competition.LeaderboardDirty)
                    .OrderBy(competition => competition.Id)
                    .Take(500)
                    .ToListAsync(cancellationToken);
                if (developmentCompetitions.Count == 0)
                    return;
                foreach (var competition in developmentCompetitions)
                {
                    competition.LeaderboardDirty = false;
                    await outbox.PublishAsync(new ProjectLeaderboard(competition.Id));
                }
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                db.ChangeTracker.Clear();
            }
        }
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var competitions = await db.Competitions
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM competitions
                    WHERE leaderboard_dirty = TRUE
                    ORDER BY id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 500
                    """)
                .ToListAsync(cancellationToken);
            if (competitions.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            foreach (var competition in competitions)
            {
                competition.LeaderboardDirty = false;
                await outbox.PublishAsync(new ProjectLeaderboard(competition.Id));
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            db.ChangeTracker.Clear();
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
        var submission = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == message.GameplayFactId,
            cancellationToken);
        if (runtime is null
            || submission is null
            || runtime.Purpose != RuntimePurpose.AwdpTarget
            || runtime.GameplayFactId != submission.Id
            || runtime.Generation != message.Generation
            || runtime.ProcessingVersion != message.RuntimeProcessingVersion
            || runtime.State != RuntimeState.Running
            || !string.Equals(runtime.RunnerPool, message.RunnerPool, StringComparison.Ordinal)
            || !string.Equals(runtime.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing)
            return;

        submission.State = NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed;
        submission.FailureCode = NoCTF.Domain.Gameplay.GameplayFactFailureCode.CheckerPlatformError;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
        runtime.State = RuntimeState.Stopping;
        runtime.RunnerAssignmentReleaseToken = null;
        runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            CompetitionEventLevel.Error,
            CompetitionEventVisibility.Team,
            submission.UpdatedAt,
            ActorUserId: submission.ActorUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: submission.Id,
            GameplayFactKind: submission.Kind,
            GameplayFactState: submission.State,
            GameplayFactResult: null,
            RuntimeState: runtime.State,
            RuntimeGeneration: runtime.Generation), cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            runtime,
            CompetitionEventLevel.Warning,
            submission.UpdatedAt,
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
            await FailAwdpSubmissionAsync(target.Instance, db, outbox, cancellationToken);
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
            await FailAwdpSubmissionAsync(target.Instance, db, outbox, cancellationToken);
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
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose != RuntimePurpose.AwdpTarget
            || instance.GameplayFactId is not Guid gameplayFactId)
            return;
        var submission = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == gameplayFactId,
            cancellationToken);
        if (submission is null
            || submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing)
            return;
        submission.State = NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed;
        submission.FailureCode = NoCTF.Domain.Gameplay.GameplayFactFailureCode.CheckerPlatformError;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
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
                        var retryAt = DateTimeOffset.UtcNow.Add(RunnerDependencyRetryDelay);
                        await outbox.ScheduleAsync(
                            message with { At = retryAt },
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
        if (pageIsFull || expiredRuntimes.Count == 500)
            await outbox.PublishAsync(new ReconcileRunnerAssignments(
                message.At,
                pageIsFull ? assignments[^1].Id : null));
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

    public static async Task Handle(
        DrainGameplayFactEvaluation message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken) =>
        await DrainGameplayFactsAsync(
            message.CompetitionId, message.CompetitionChallengeId, message.Cutoff,
            rejudge: false, message.GameplayFactId, message, db, outbox, cancellationToken);

    public static async Task Handle(
        DrainGameplayFactRejudge message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken) =>
        await DrainGameplayFactsAsync(
            message.CompetitionId, message.CompetitionChallengeId, message.Cutoff,
            rejudge: true, message.GameplayFactId, message, db, outbox, cancellationToken);

    private static async Task DrainGameplayFactsAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? requestedGameplayFactId,
        object continuationMessage,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var candidates = db.GameplayFacts.Where(submission =>
            submission.CompetitionId == competitionId
            && submission.CompetitionChallengeId == competitionChallengeId
            && submission.OccurredAt <= cutoff);
        if (requestedGameplayFactId is Guid gameplayFactId)
        {
            candidates = candidates.Where(submission =>
                submission.Id == gameplayFactId
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Queued
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing);
        }
        else if (rejudge)
        {
            candidates = candidates.Where(submission =>
                submission.Result != null
                && (submission.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt
                    || submission.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.BreakAttempt)
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Queued
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing);
        }
        else
        {
            candidates = candidates.Where(submission =>
                submission.State == NoCTF.Domain.Gameplay.GameplayFactState.Pending
                || (submission.State == NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed
                    && submission.Result == null));
        }

        var candidateIds = await candidates.AsNoTracking()
            .OrderBy(submission => submission.OccurredAt)
            .ThenBy(submission => submission.Id)
            .Take(requestedGameplayFactId is null ? batchSize : 1)
            .Select(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        await candidates.Where(submission => candidateIds.Contains(submission.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    submission => submission.State,
                    NoCTF.Domain.Gameplay.GameplayFactState.Queued)
                .SetProperty(submission => submission.UpdatedAt, now),
                cancellationToken);
        var submissions = await db.GameplayFacts.AsNoTracking()
            .Where(submission => candidateIds.Contains(submission.Id))
            .OrderBy(submission => submission.OccurredAt)
            .ThenBy(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var submission in submissions)
        {
            await outbox.PublishAsync(new EvaluateGameplayFact(submission.Id));
        }
        if (candidateIds.Length == batchSize && requestedGameplayFactId is null)
            await outbox.PublishAsync(continuationMessage);
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
            GameplayFactId: instance.GameplayFactId,
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
