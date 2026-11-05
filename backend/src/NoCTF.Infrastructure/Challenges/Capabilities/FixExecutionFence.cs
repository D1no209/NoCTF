using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

public sealed class FixExecutionFence(
    NoCtfDbContext db,
    TimeProvider timeProvider) : IFixExecutionFence
{
    public async Task<FixExecutionFenceResult> AcquireAsync(
        FixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        try { return await AcquireCoreAsync(request, cancellationToken); }
        catch (Exception exception) when (TransactionFailureClassifier.IsRetryable(exception)
            || exception.GetBaseException() is System.Data.Common.DbException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            return FixExecutionFenceResult.Superseded(request);
        }
    }

    private async Task<FixExecutionFenceResult> AcquireCoreAsync(
        FixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == request.RuntimeInstanceId,
            cancellationToken);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == request.GameplayFactId,
            cancellationToken);
        if (!IsSameOperation(runtime, fact, request))
            return FixExecutionFenceResult.Superseded(request);

        if (runtime!.State == RuntimeState.Running)
        {
            if (timeProvider.GetUtcNow() >= request.Deadline)
                return FixExecutionFenceResult.Superseded(request);
            if (fact!.FixExecution is { Phase: not FixExecutionPhase.Queued })
                return FixExecutionFenceResult.Superseded(request);
            if (fact.FixExecution is { } execution && (execution.RuntimeInstanceId != request.RuntimeInstanceId
                    || execution.Deadline != request.Deadline))
                return FixExecutionFenceResult.Superseded(request);
            fact.FixExecution ??= new()
            {
                GameplayFactId = fact.Id, RuntimeInstanceId = runtime.Id,
                TargetKind = FixTargetKind.Isolated, Deadline = request.Deadline
            };
            fact.FixExecution.Phase = FixExecutionPhase.Applying;
            fact.FixExecution.MutationBlocked = true;
            fact.FixExecution.StartedAt = timeProvider.GetUtcNow();
            fact.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(FixExecutionFenceDisposition.Execute, runtime);
        }

        if (runtime.State == RuntimeState.Stopping)
        {
            await transaction.CommitAsync(cancellationToken);
            return Result(FixExecutionFenceDisposition.Recover, runtime);
        }

        return FixExecutionFenceResult.Superseded(request);
    }

    private static bool IsSameOperation(
        RuntimeInstance? runtime,
        GameplayFact? fact,
        FixExecutionFenceRequest request) =>
        runtime is { GameplayFactId: not null, RunnerId: not null }
        && (runtime.Purpose is RuntimePurpose.FixTarget
            or RuntimePurpose.ScopedFixTarget)
        && runtime.GameplayFactId == request.GameplayFactId
        && runtime.CompetitionChallengeId == request.CompetitionChallengeId
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

    private static FixExecutionFenceResult Result(
        FixExecutionFenceDisposition disposition,
        RuntimeInstance runtime) =>
        new(
            disposition,
            runtime.Id,
            runtime.RuntimeProvider,
            runtime.ProviderReceipt?.ToData(),
            runtime.RunnerId!);
}
