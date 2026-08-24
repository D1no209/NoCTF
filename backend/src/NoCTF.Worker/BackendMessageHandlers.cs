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
using NoCTF.GameModes.Awd.Scheduling;
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

public static class BackendMessageHandlers
{
    private static readonly TimeSpan RunnerDependencyRetryDelay = TimeSpan.FromSeconds(5);

    public static async Task Handle(
        CleanupFile message,
        NoCtfDbContext db,
        IStore objects,
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
            || await db.PatchUploads.AnyAsync(item => item.FileId == file.Id, cancellationToken);
        if (referenced)
            return;
        await objects.DeleteObject(file.ObjectKey, cancellationToken);
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
                && runtime.RunnerId != null
                && runtime.ProviderReceiptJson != null
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
                continue;

            var lastCheckAt = await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == target.Runtime.CompetitionId
                    && fact.CompetitionChallengeId == target.Runtime.CompetitionChallengeId
                    && fact.TeamId == target.Runtime.TeamId
                    && fact.Kind == GameplayFactKind.AwdServiceTransition)
                .MaxAsync(fact => (DateTimeOffset?)fact.OccurredAt, cancellationToken);
            if (lastCheckAt is { } previous
                && previous.AddSeconds(settings.CheckerIntervalSeconds) > message.At)
                continue;

            var factId = CreateAwdCheckerFactId(target.Runtime.Id, message.At);
            var factExists = await db.GameplayFacts.AsNoTracking()
                .AnyAsync(fact => fact.Id == factId, cancellationToken);
            if (factExists)
                continue;

            var deadline = message.At.AddSeconds(checker.TimeoutSeconds);
            db.GameplayFacts.Add(new GameplayFact
            {
                Id = factId,
                CompetitionId = target.Runtime.CompetitionId,
                CompetitionChallengeId = target.Runtime.CompetitionChallengeId,
                TeamId = target.Runtime.TeamId,
                Kind = GameplayFactKind.AwdServiceTransition,
                OccurredAt = message.At,
                State = GameplayFactState.Processing,
                UpdatedAt = message.At
            });
            await outbox.PublishToRunnerNodeAsync(new RunAwdChecker(
                target.Runtime.Id,
                target.Runtime.CompetitionChallengeId,
                factId,
                target.Runtime.RunnerId!,
                deadline));
            await outbox.ScheduleAsync(new AwdCheckerCallbackMissing(
                target.Runtime.CompetitionId,
                target.Runtime.CompetitionChallengeId,
                target.Runtime.Id,
                factId,
                deadline), deadline);
            applied = true;
        }

        var pageIsFull = targets.Count == batchSize;
        if (pageIsFull)
            await outbox.PublishAsync(new DispatchAwdCheckers(message.At, targets[^1].Runtime.Id));
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return applied ? MessageExecutionOutcome.Applied : MessageExecutionOutcome.Idempotent;
    }

    private static Guid CreateAwdCheckerFactId(Guid runtimeInstanceId, DateTimeOffset occurredAt)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], occurredAt.UtcTicks);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
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
            message.ChallengeFlagId
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
        IInternalResultStore results,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var disposition = await results.RecordAwdAsync(AwdCheckResult.Create(
            message.RuntimeInstanceId,
            message.GameplayFactId,
            AwdServiceState.CheckerTimedOut,
            message.OccurredAt), cancellationToken);
        if (disposition != InternalResultDisposition.Applied)
            return;

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
                && notification.RelatedType == EntityReferenceKind.GameplayFact
                && notification.RelatedId == message.GameplayFactId
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
            message.GameplayFactId
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(message.OccurredAt),
            SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators,
            TargetId = message.CompetitionId,
            Kind = NotificationKind.ManagementFailure,
            ContentJson = payload,
            RelatedType = EntityReferenceKind.GameplayFact,
            RelatedId = message.GameplayFactId,
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
        // Capacity reconciliation remains durable maintenance work, but lifecycle is
        // the only periodic trigger owned by the Singular Agent.
        await outbox.PublishAsync(new ReconcileRunnerAssignments(message.At));
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
        CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task Handle(
        RefreshDirtyLeaderboards message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ILeaderboardCache leaderboard,
        CancellationToken cancellationToken)
    {
        await MarkDueAwdpLeaderboardsAsync(
            message.TriggeredAt,
            db,
            leaderboard,
            cancellationToken);
        if (db.Database.IsInMemory())
        {
            while (true)
            {
                var developmentCompetitions = await db.Competitions
                    .Where(_ => false)
                    .OrderBy(competition => competition.Id)
                    .Take(500)
                    .ToListAsync(cancellationToken);
                if (developmentCompetitions.Count == 0)
                    return;
                foreach (var competition in developmentCompetitions)
                {
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
                .Where(_ => false)
                .ToListAsync(cancellationToken);
            if (competitions.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
            foreach (var competition in competitions)
            {
                await outbox.PublishAsync(new ProjectLeaderboard(competition.Id));
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private static async Task MarkDueAwdpLeaderboardsAsync(
        DateTimeOffset now,
        NoCtfDbContext db,
        ILeaderboardCache leaderboard,
        CancellationToken cancellationToken)
    {
        Guid? afterCompetitionId = null;
        while (true)
        {
            var competitions = await db.Competitions
                .Where(competition => competition.Mode == GameMode.Awdp
                    && competition.Status == CompetitionStatus.Running
                    && competition.DeletedAt == null
                    && (afterCompetitionId == null
                        || competition.Id.CompareTo(afterCompetitionId.Value) > 0))
                .OrderBy(competition => competition.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            if (competitions.Count == 0)
                return;
            afterCompetitionId = competitions[^1].Id;
            await MarkDueAwdpLeaderboardBatchAsync(
                competitions,
                now,
                db,
                leaderboard,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            if (competitions.Count < 500)
                return;
        }
    }

    private static async Task MarkDueAwdpLeaderboardBatchAsync(
        IReadOnlyList<Competition> competitions,
        DateTimeOffset now,
        NoCtfDbContext db,
        ILeaderboardCache leaderboard,
        CancellationToken cancellationToken)
    {
        var continuous = competitions
            .Select(competition => new
            {
                Competition = competition,
                Configuration = TryParseAwdpCompetition(competition.ConfigurationJson)
            })
            .Where(item => item.Configuration is not null)
            .ToArray();
        if (continuous.Length == 0)
            return;

        var competitionIds = continuous.Select(item => item.Competition.Id).ToArray();
        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => competitionIds.Contains(@event.CompetitionId)
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                && @event.OccurredAt <= now)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .Select(@event => new
            {
                @event.Id,
                @event.CompetitionId,
                @event.OccurredAt,
                @event.ActorUserId,
                @event.PayloadJson
            })
            .ToListAsync(cancellationToken);
        var transitions = lifecycleEvents
            .Select(@event =>
            {
                var payload = JsonSerializer.Deserialize<CompetitionLifecyclePayload>(
                    @event.PayloadJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException(
                        "Competition lifecycle event payload is invalid.");
                return new CompetitionLifecycleTransition
                {
                    Id = @event.Id,
                    CompetitionId = @event.CompetitionId,
                    From = payload.From,
                    To = payload.To,
                    ActorId = @event.ActorUserId,
                    Reason = payload.Reason,
                    Automatic = payload.Automatic,
                    OccurredAt = @event.OccurredAt
                };
            })
            .GroupBy(transition => transition.CompetitionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CompetitionLifecycleTransition>)group.ToArray());

        foreach (var item in continuous)
        {
            var snapshot = await leaderboard.GetAsync(
                item.Competition.Id,
                cancellationToken);
            if (snapshot?.DataAsOf is not DateTimeOffset dataAsOf)
                continue;
            var competitionTransitions = transitions.GetValueOrDefault(item.Competition.Id) ?? [];
            var duration = item.Configuration!.RoundDurationSeconds;
            var projectedRound = LogicalAwdpRound(
                competitionTransitions,
                dataAsOf,
                duration);
            var currentRound = LogicalAwdpRound(
                competitionTransitions,
                now,
                duration);
            _ = currentRound > projectedRound;
        }
    }

    private static int LogicalAwdpRound(
        IReadOnlyList<CompetitionLifecycleTransition> transitions,
        DateTimeOffset at,
        int durationSeconds)
    {
        var elapsed = AwdEffectiveRunningClock.Calculate(transitions, at);
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        return checked((int)(seconds / durationSeconds) + 1);
    }

    private static AwdpConfiguration? TryParseAwdpCompetition(string json)
    {
        try
        {
            return AwdpConfigurationParser.ParseCompetition(json);
        }
        catch (GameModeConfigurationException)
        {
            return null;
        }
    }

    private sealed record CompetitionLifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);

    public static Task Handle(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken) =>
        results.RecordAwdpAsync(message, cancellationToken);

    public static async Task Handle(
        CompleteAwdpFixRecovery message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = await db.RuntimeInstances
            .FromSqlInterpolated($"""
                SELECT *
                FROM runtime_instances
                WHERE id = {message.RuntimeInstanceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == message.GameplayFactId,
            cancellationToken);
        if (previous is null
            || fact is null
            || previous.Purpose != RuntimePurpose.AwdpTarget
            || previous.GameplayFactId != fact.Id
            || previous.State != RuntimeState.Stopping
            || !string.Equals(previous.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return;

        previous.State = RuntimeState.Stopped;
        previous.StoppedAt = message.CleanedAt;
        await RecordRuntimeStateAsync(
            events,
            previous,
            CompetitionEventLevel.Warning,
            message.CleanedAt,
            cancellationToken);

        if (fact.State != GameplayFactState.Processing)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        fact.State = GameplayFactState.PlatformFailed;
        fact.FailureCode = GameplayFactFailureCode.CheckerPlatformError;
        fact.UpdatedAt = message.CleanedAt;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await QueueNextGameplayFactAsync(fact, db, outbox, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

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
            || runtime.State != RuntimeState.Running
            || !string.Equals(runtime.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing)
            return;

        submission.State = NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed;
        submission.FailureCode = NoCTF.Domain.Gameplay.GameplayFactFailureCode.CheckerPlatformError;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
        runtime.State = RuntimeState.Stopping;
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
            RuntimeState: runtime.State), cancellationToken);
        await RecordRuntimeStateAsync(
            events,
            runtime,
            CompetitionEventLevel.Warning,
            submission.UpdatedAt,
            cancellationToken);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            message.RunnerId));
        await QueueNextGameplayFactAsync(submission, db, outbox, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        IRunnerCapacityGate capacity,
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
        if (target is null || target.Instance.State != RuntimeState.Queued)
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
        if (target.Competition.Mode is GameMode.Ctf or GameMode.Awdp
            && target.Instance.Purpose != RuntimePurpose.AwdpTarget
            && template.FlagSource == RuntimeFlagSource.PerTeam)
        {
            if (target.Instance.TeamId is Guid teamId)
            {
                var specificationKind = target.Competition.Mode == GameMode.Awdp
                    ? SpecificationKind.RuntimeInstance
                    : SpecificationKind.RuntimeDefinition;
                var specificationId = target.Competition.Mode == GameMode.Awdp
                    ? target.Instance.Id
                    : target.Challenge.Id;
                perTeamFlag = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.CompetitionChallengeId == target.Challenge.Id
                        && flag.TeamId == teamId
                        && flag.SpecificationKind == specificationKind
                        && flag.SpecificationId == specificationId
                        && flag.DeletedAt == null
                        && flag.ValidUntil == null)
                    .Select(flag => flag.Flag)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            if (perTeamFlag is null)
            {
                target.Instance.State = RuntimeState.Failed;
                target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
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

        var placement = placementPolicy.Resolve(target.Instance.RuntimeKind);
        if (placement.Provider != target.Instance.RuntimeProvider)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            await FailAwdpSubmissionAsync(target.Instance, db, outbox, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var limits = template.Limits
            ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256);
        var capacityClaim = await capacity.TryClaimAsync(new RunnerCapacityRequest(
            target.Instance.Id,
            placement.RunnerPool,
            limits.MemoryBytes,
            limits.NanoCpus,
            limits.PidsLimit), cancellationToken);
        if (capacityClaim.Availability != RunnerCapacityAvailability.Claimed
            || string.IsNullOrWhiteSpace(capacityClaim.RunnerId))
        {
            var retryAt = DateTimeOffset.UtcNow.Add(RunnerDependencyRetryDelay);
            await outbox.ScheduleAsync(message, retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var runnerId = capacityClaim.RunnerId;
        IRuntimeProvisionMessage provision;
        try
        {
            if (target.Instance.Purpose == RuntimePurpose.AwdpTarget)
            {
                var definition = AwdpTargetDefinitionFactory.Create(
                    target.Instance.Id,
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
                provision = new ProvisionContainerRuntime(
                    target.Instance.Id,
                    runnerId,
                    definition);
            }
            else
            {
                provision = RuntimeClaimFactory.Create(
                    target.Instance,
                    runnerId,
                    target.Competition.Mode,
                    template,
                    target.Template.DefinitionJson,
                    perTeamFlag);
            }
        }
        catch (InvalidOperationException)
        {
            var release = await capacity.ReleaseAsync(
                target.Instance.Id,
                runnerId,
                cancellationToken);
            if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            {
                throw new InvalidOperationException(
                    "The selected Runner no longer owns the Runtime capacity claim.");
            }
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
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

        target.Instance.RunnerId = runnerId;
        target.Instance.State = RuntimeState.Provisioning;
        target.Instance.FailureCode = null;
        await PublishRuntimeProvisionAsync(outbox, provision);
        await db.SaveChangesAsync(cancellationToken);
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
        await QueueNextGameplayFactAsync(submission, db, outbox, cancellationToken);
    }

    private static async Task QueueNextGameplayFactAsync(
        GameplayFact completed,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var nextGameplayFactId = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == completed.CompetitionId
                && candidate.CompetitionChallengeId == completed.CompetitionChallengeId
                && candidate.TeamId == completed.TeamId
                && candidate.Kind == completed.Kind
                && candidate.State == GameplayFactState.Queued)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
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
        if (instance is null || instance.State != RuntimeState.Stopping)
            return;
        if (string.IsNullOrWhiteSpace(instance.ProviderReceiptJson))
        {
            if (instance.RunnerId is { } runnerId)
            {
                await PublishRuntimeStopAsync(outbox, instance, runnerId);
                await outbox.FlushOutgoingMessagesAsync();
                return;
            }

            instance.State = RuntimeState.Stopped;
            instance.StoppedAt = DateTimeOffset.UtcNow;
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
                await RecordRuntimeStateAsync(
                    events,
                    instance,
                    CompetitionEventLevel.Information,
                    now,
                    cancellationToken);
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
                && instance.State == RuntimeState.Queued)
            .OrderBy(instance => instance.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var instance in queued)
            await outbox.PublishAsync(new DispatchRuntime(instance.Id));
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteRunnerAssignmentReconciliationAsync(
            message,
            db,
            capacity,
            placementPolicy,
            outbox,
            cancellationToken);
    }

    public static async Task<MessageExecutionOutcome> ExecuteRunnerAssignmentReconciliationAsync(
        ReconcileRunnerAssignments message,
        NoCtfDbContext db,
        IRunnerCapacityGate capacity,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
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
                    && instance.RunnerId != null)
                .Select(instance => instance.RunnerId!)
                .Distinct()
                .ToListAsync(cancellationToken);
            var failedCleanupOwnerSet = failedCleanupOwners.ToHashSet(StringComparer.Ordinal);
            var runtimePools = Enum.GetValues<RuntimeKind>()
                .Select(kind => placementPolicy.Resolve(kind).RunnerPool)
                .Distinct()
                .OrderBy(pool => pool)
                .ToArray();
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
            RuntimeState: instance.State),
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
