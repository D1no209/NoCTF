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
