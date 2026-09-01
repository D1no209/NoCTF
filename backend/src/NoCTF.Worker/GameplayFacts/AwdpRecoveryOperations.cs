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
    private static readonly TimeSpan AwdpFixReadinessRetryDelay = TimeSpan.FromSeconds(1);

    public static async Task StartAwdpFixVerificationAsync(
        StartAwdpFixVerification message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances
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
        if (fact is not
            {
                Kind: GameplayFactKind.FixAttempt,
                State: GameplayFactState.Pending,
                ReferenceKind: GameplayFactReferenceKind.PatchUpload,
                ReferenceId: not null
            }
            || runtime is not
            {
                Purpose: RuntimePurpose.AwdpTarget,
                GameplayFactId: not null
            }
            || runtime.GameplayFactId != fact.Id
            || runtime.Id != message.RuntimeInstanceId
            || runtime.CompetitionChallengeId != fact.CompetitionChallengeId
            || runtime.TeamId != fact.TeamId)
        {
            return;
        }

        if (runtime.State is RuntimeState.Queued or RuntimeState.Provisioning)
        {
            await transaction.CommitAsync(cancellationToken);
            await outbox.ScheduleAsync(
                message,
                timeProvider.GetUtcNow().Add(AwdpFixReadinessRetryDelay));
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (runtime.State != RuntimeState.Running
            || string.IsNullOrWhiteSpace(runtime.RunnerId))
        {
            await FailPendingAwdpFixAsync(
                fact,
                runtime,
                db,
                outbox,
                events,
                now,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var patchUploadExists = await db.PatchUploads.AsNoTracking().AnyAsync(
            upload => upload.Id == fact.ReferenceId.Value
                && upload.RuntimeInstanceId == runtime.Id
                && upload.CompetitionChallengeId == runtime.CompetitionChallengeId
                && upload.TeamId == runtime.TeamId,
            cancellationToken);
        if (!patchUploadExists)
        {
            await FailPendingAwdpFixAsync(
                fact,
                runtime,
                db,
                outbox,
                events,
                now,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var deadline = runtime.ExpiresAt is { } expiresAt && expiresAt > now
            ? expiresAt
            : now.AddMinutes(15);
        runtime.ExpiresAt = deadline;
        fact.State = GameplayFactState.Processing;
        fact.UpdatedAt = now;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await outbox.PublishToRunnerNodeAsync(new RunAwdpFixVerification(
            fact.Id,
            fact.CompetitionChallengeId,
            fact.ReferenceId.Value,
            runtime.Id,
            deadline,
            runtime.RunnerId));
        await outbox.ScheduleAsync(new ExpireAwdpFixVerification(
            fact.Id,
            runtime.Id,
            deadline,
            runtime.RunnerId), deadline);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async Task FailPendingAwdpFixAsync(
        GameplayFact fact,
        RuntimeInstance runtime,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder events,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        fact.State = GameplayFactState.PlatformFailed;
        fact.FailureCode = GameplayFactFailureCode.CheckerPlatformError;
        fact.UpdatedAt = failedAt;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            CompetitionEventLevel.Error,
            CompetitionEventVisibility.Team,
            failedAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            RuntimeState: runtime.State), cancellationToken);
        if (fact.ReferenceId is Guid patchUploadId
            && fact.TeamId is Guid teamId)
        {
            var payload = AwdpFixResolvedEventPayload.Create(
                fact.Id,
                patchUploadId,
                runtime.Id,
                teamId,
                fact.CompetitionChallengeId,
                AwdpFixOutcome.PlatformFailed,
                fact.FailureCode,
                failedAt);
            await events.RecordAsync(new(
                fact.CompetitionId,
                CompetitionEventKind.AwdpFixResolved,
                CompetitionEventLevel.Error,
                CompetitionEventVisibility.Public,
                failedAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactState: fact.State,
                PayloadJson: payload.Serialize()), cancellationToken);
        }
        await QueueNextGameplayFactAsync(fact, db, outbox, cancellationToken);
    }

    public static Task RecordAwdpFixResultAsync(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken) =>
        results.RecordAwdpAsync(message, cancellationToken);

    public static async Task CompleteAwdpFixRecoveryAsync(
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

    public static async Task ExpireAwdpFixVerificationAsync(
        ExpireAwdpFixVerification message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        if (timeProvider.GetUtcNow() < message.Deadline)
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
        submission.UpdatedAt = timeProvider.GetUtcNow();
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

}
