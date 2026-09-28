using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class AwdpFixExecutionFence(
    NoCtfDbContext db,
    TimeProvider timeProvider) : IAwdpFixExecutionFence
{
    public async Task<AwdpFixExecutionFenceResult> AcquireAsync(
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == request.RuntimeInstanceId,
            cancellationToken);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            candidate => candidate.Id == request.GameplayFactId,
            cancellationToken);
        if (!IsSameOperation(runtime, fact, request))
            return AwdpFixExecutionFenceResult.Superseded(request);

        if (runtime!.State == RuntimeState.Running)
        {
            if (timeProvider.GetUtcNow() >= request.Deadline)
                return AwdpFixExecutionFenceResult.Superseded(request);

            await transaction.CommitAsync(cancellationToken);
            return Result(AwdpFixExecutionFenceDisposition.Execute, runtime);
        }

        if (runtime.State == RuntimeState.Stopping)
        {
            await transaction.CommitAsync(cancellationToken);
            return Result(AwdpFixExecutionFenceDisposition.Recover, runtime);
        }

        return AwdpFixExecutionFenceResult.Superseded(request);
    }

    private static bool IsSameOperation(
        RuntimeInstance? runtime,
        GameplayFact? fact,
        AwdpFixExecutionFenceRequest request) =>
        runtime is { GameplayFactId: not null, RunnerId: not null }
        && (runtime.Purpose is RuntimePurpose.AwdpTarget
            or RuntimePurpose.PatchVerificationTarget)
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

    private static AwdpFixExecutionFenceResult Result(
        AwdpFixExecutionFenceDisposition disposition,
        RuntimeInstance runtime) =>
        new(
            disposition,
            runtime.Id,
            runtime.RuntimeProvider,
            runtime.ProviderReceipt?.ToData(),
            runtime.RunnerId!);
}
