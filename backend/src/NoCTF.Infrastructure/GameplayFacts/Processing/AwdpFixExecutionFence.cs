using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class PostgresAwdpFixExecutionFence(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IAwdpFixExecutionFence
{
    public async Task<AwdpFixExecutionFenceResult> AcquireAsync(
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances
            .FromSqlInterpolated($"""
                SELECT *
                FROM runtime_instances
                WHERE id = {request.RuntimeInstanceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == request.GameplayFactId,
            cancellationToken);
        if (!IsSameOperation(runtime, fact, request))
            return AwdpFixExecutionFenceResult.Superseded(request);

        if (runtime!.State == RuntimeState.Running
            && runtime.AwdpFixStage == AwdpFixStage.PatchApplying
            && runtime.ProcessingVersion == request.RuntimeProcessingVersion)
        {
            if (timeProvider.GetUtcNow() >= request.Deadline)
                return AwdpFixExecutionFenceResult.Superseded(request);

            runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
            await outbox.ScheduleAsync(new ExpireAwdpFixVerification(
                request.GameplayFactId,
                runtime.Id,
                runtime.Generation,
                runtime.ProcessingVersion,
                request.Deadline,
                runtime.RunnerPool,
                runtime.RunnerId!), request.Deadline);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return Result(AwdpFixExecutionFenceDisposition.Execute, runtime);
        }

        if (runtime.State == RuntimeState.Running
            && runtime.ProcessingVersion == checked(request.RuntimeProcessingVersion + 1))
        {
            runtime.State = RuntimeState.Stopping;
            runtime.RunnerAssignmentReleaseToken = null;
            runtime.ProcessingVersion = checked(runtime.ProcessingVersion + 1);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(AwdpFixExecutionFenceDisposition.Recover, runtime);
        }

        if (runtime.State == RuntimeState.Stopping
            && runtime.ProcessingVersion == checked(request.RuntimeProcessingVersion + 2))
        {
            await transaction.CommitAsync(cancellationToken);
            return Result(AwdpFixExecutionFenceDisposition.Recover, runtime);
        }

        return AwdpFixExecutionFenceResult.Superseded(request);
    }

    public async Task<bool> TryAdvanceStageAsync(
        AwdpFixStageTransitionRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances
            .FromSqlInterpolated($"""
                SELECT *
                FROM runtime_instances
                WHERE id = {request.RuntimeInstanceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (runtime is not
            {
                Purpose: RuntimePurpose.AwdpTarget,
                State: RuntimeState.Running,
                GameplayFactId: not null
            }
            || runtime.GameplayFactId != request.GameplayFactId
            || runtime.Generation != request.Generation
            || runtime.ProcessingVersion != request.RuntimeProcessingVersion)
        {
            return false;
        }

        if (runtime.AwdpFixStage == request.NextStage)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        if (runtime.AwdpFixStage != request.ExpectedStage)
            return false;

        runtime.AwdpFixStage = request.NextStage;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsSameOperation(
        RuntimeInstance? runtime,
        GameplayFact? fact,
        AwdpFixExecutionFenceRequest request) =>
        runtime is
        {
            Purpose: RuntimePurpose.AwdpTarget,
            GameplayFactId: not null,
            RunnerId: not null
        }
        && runtime.GameplayFactId == request.GameplayFactId
        && runtime.CompetitionChallengeId == request.CompetitionChallengeId
        && runtime.Generation == request.Generation
        && string.Equals(runtime.RunnerPool, request.RunnerPool, StringComparison.Ordinal)
        && string.Equals(runtime.RunnerId, request.RunnerId, StringComparison.Ordinal)
        && fact is
        {
            Kind: GameplayFactKind.FixAttempt,
            State: GameplayFactState.Processing,
            ReferenceKind: GameplayFactReferenceKind.PatchUpload
        }
        && fact.Id == runtime.GameplayFactId
        && fact.CompetitionChallengeId == request.CompetitionChallengeId
        && fact.ReferenceId == request.PatchUploadId;

    private static AwdpFixExecutionFenceResult Result(
        AwdpFixExecutionFenceDisposition disposition,
        RuntimeInstance runtime) =>
        new(
            disposition,
            runtime.Id,
            runtime.Generation,
            runtime.ProcessingVersion,
            runtime.RuntimeProvider,
            runtime.ProviderReceiptJson,
            runtime.RunnerPool,
            runtime.RunnerId!);
}
