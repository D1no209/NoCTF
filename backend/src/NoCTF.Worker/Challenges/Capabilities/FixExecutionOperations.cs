using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.GameplayFacts.Awdp;
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
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Challenges.Capabilities;
using NoCTF.Application.Challenges.Capabilities;
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

    public static async Task StartFixExecutionAsync(
        StartFixExecution message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == message.RuntimeInstanceId,
            cancellationToken);
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
                GameplayFactId: not null
            }
            || runtime.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
            || runtime.GameplayFactId != fact.Id
            || runtime.Id != message.RuntimeInstanceId
            || runtime.CompetitionChallengeId != fact.CompetitionChallengeId
            || runtime.TeamId != fact.TeamId)
        {
            return;
        }

        if (fact.FixExecution is { } admittedExecution && timeProvider.GetUtcNow() >= admittedExecution.Deadline)
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(runtime, db, outbox, events, timeProvider.GetUtcNow(),
                AwdpFixRuntimeCleanupMode.EnsureStop, cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync(); return;
        }
        if (runtime.State is RuntimeState.Queued or RuntimeState.Provisioning)
        {
            await transaction.CommitAsync(cancellationToken);
            await outbox.ScheduleAsync(
                message,
                timeProvider.GetUtcNow().Add(AwdpFixReadinessRetryDelay));
            await outbox.FlushCommittedMessagesAsync();
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (runtime.State != RuntimeState.Running
            || string.IsNullOrWhiteSpace(runtime.RunnerId))
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                runtime,
                db,
                outbox,
                events,
                now,
                AwdpFixRuntimeCleanupMode.EnsureStop,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync();
            return;
        }

        var patchUpload = await db.PatchUploads.AsNoTracking().SingleOrDefaultAsync(
            upload => upload.Id == fact.ReferenceId.Value
                && upload.RuntimeInstanceId == runtime.Id
                && upload.CompetitionChallengeId == runtime.CompetitionChallengeId
                && upload.TeamId == runtime.TeamId,
            cancellationToken);
        if (patchUpload is null)
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                runtime,
                db,
                outbox,
                events,
                now,
                AwdpFixRuntimeCleanupMode.EnsureStop,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync();
            return;
        }

        var configurationContext = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == fact.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    Rules = challenge.Rules!,
                    Definition = template.Definition!,
                    challenge.CompetitionId
                })
            .Join(
                db.Competitions.AsNoTracking(),
                item => item.CompetitionId,
                competition => competition.Id,
                (item, competition) => new
                {
                    Configuration = competition.ModeConfiguration!,
                    competition.Mode,
                    item.Rules,
                    item.Definition
                })
            .AsSplitQuery()
            .SingleAsync(cancellationToken);
        var configuration = FixSubmissionConfigurationResolver.Resolve(
            configurationContext.Mode,
            configurationContext.Configuration,
            configurationContext.Rules,
            configurationContext.Definition);
        if (configuration is null
            || configuration.PatchTimeoutSeconds is < 1
                or > FixExecutionBudget.MaximumPatchTimeoutSeconds
            || configuration.Checker.TimeoutSeconds is < 1
                or > FixExecutionBudget.MaximumCheckerTimeoutSeconds
            || configuration.ReadyTimeoutSeconds > configuration.Checker.TimeoutSeconds
            || !FixExecutionBudget.FitsHandlerTimeout(
                    configuration.PatchTimeoutSeconds,
                    configuration.Checker.TimeoutSeconds))
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                runtime,
                db,
                outbox,
                events,
                now,
                AwdpFixRuntimeCleanupMode.EnsureStop,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync();
            return;
        }
        var deadline = FixExecutionBudget.CalculateDeadline(
            patchUpload.UploadedAt,
            runtime.ExpiresAt,
            configuration.PatchTimeoutSeconds,
            configuration.Checker.TimeoutSeconds);
        if (fact.FixExecution is { } accepted && accepted.Deadline < deadline) deadline = accepted.Deadline;
        deadline = deadline.AddTicks(-(deadline.Ticks % TimeSpan.TicksPerMicrosecond));
        if (deadline <= now)
        {
            await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
                runtime,
                db,
                outbox,
                events,
                now,
                AwdpFixRuntimeCleanupMode.EnsureStop,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushCommittedMessagesAsync();
            return;
        }
        fact.State = GameplayFactState.Processing;
        fact.UpdatedAt = now;
        fact.FixExecution ??= new()
        {
            GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id, Deadline = deadline,
            TargetKind = NoCTF.Domain.Challenges.Capabilities.FixTargetKind.Isolated,
            Phase = NoCTF.Domain.Challenges.Capabilities.FixExecutionPhase.Queued
        };
        fact.FixExecution.Deadline = deadline;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await outbox.PublishToRunnerNodeAsync(new RunFixExecution(
            fact.Id,
            fact.CompetitionChallengeId!.Value,
            fact.ReferenceId.Value,
            runtime.Id,
            deadline,
            runtime.RunnerId));
        await outbox.ScheduleAsync(new ExpireFixExecution(
            fact.Id,
            runtime.Id,
            deadline,
            runtime.RunnerId), deadline);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
    }

    public static Task RecordAwdpFixResultAsync(
        AwdpFixResult message,
        IInternalResultStore results,
        CancellationToken cancellationToken) =>
        results.RecordAwdpAsync(message, cancellationToken);

    public static async Task RecoverFixExecutionAsync(
        RecoverFixExecution message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == message.RuntimeInstanceId,
            cancellationToken);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == message.GameplayFactId,
            cancellationToken);
        if (previous is null
            || fact is null
            || previous.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
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

        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            previous,
            db,
            outbox,
            events,
            message.CleanedAt,
            AwdpFixRuntimeCleanupMode.CallerManaged,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
    }

    public static async Task ExpireFixExecutionAsync(
        ExpireFixExecution message,
        NoCtfDbContext db,
        IPostCommitMessagePublisher outbox,
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
            || runtime.Purpose is not (RuntimePurpose.FixTarget
                or RuntimePurpose.ScopedFixTarget)
            || runtime.GameplayFactId != submission.Id
            || (runtime.RunnerId is not null
                && !string.Equals(runtime.RunnerId, message.RunnerId, StringComparison.Ordinal)))
            return;
        if (submission.FixExecution is { Phase: NoCTF.Domain.Challenges.Capabilities.FixExecutionPhase.Finalizing,
            WorkCompletedAt: not null, ExecutionFailure: null })
        {
            await outbox.PublishAsync(new FinalizeFixExecution(submission.Id, runtime.Id));
            await transaction.CommitAsync(cancellationToken); await outbox.FlushCommittedMessagesAsync();
            return;
        }
        if (submission.FixExecution is { } timedOut)
        {
            timedOut.FailedPhase ??= timedOut.Phase;
            timedOut.ExecutionFailure ??= NoCTF.Domain.Challenges.Capabilities.CapabilityExecutionFailure.DeadlineExceeded;
        }
        await AwdpFixFailureConvergence.ConvergeAwdpFixFailureAsync(
            runtime,
            db,
            outbox,
            events,
            timeProvider.GetUtcNow(),
            AwdpFixRuntimeCleanupMode.EnsureStop,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
    }

}
