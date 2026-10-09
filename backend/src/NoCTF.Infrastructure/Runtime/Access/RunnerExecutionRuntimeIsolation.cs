using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Infrastructure.Runtime.Access;

/// <summary>Fresh fenced Runner capability permits preparation; Running additionally requires its provider-bound receipt.</summary>
public sealed class RunnerExecutionRuntimeIsolation(NoCtfDbContext db, NatsRunnerAvailabilityRegistry runners, IRuntimePlacementPolicy placement)
    : IExecutionRuntimeIsolation
{
    public async Task<ExecutionIsolationAssessment> AssessAsync(ExecutionIsolationRequest request, CancellationToken ct)
    {
        if (request.Kind != RuntimeKind.Container || request.ExecutionScopeId == Guid.Empty || request.TeamId == Guid.Empty)
            return new(ExecutionIsolationStatus.Unsupported);
        var target = placement.Resolve(request.Kind);
        if (target.Provider != request.Provider) return new(ExecutionIsolationStatus.Unsupported);
        var current = request.RuntimeInstanceId is Guid id
            ? await db.RuntimeInstances.AsNoTracking().Include(x => x.ProviderReceipt).SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (request.RuntimeInstanceId is not null && (current is null || current.ExecutionScopeId != request.ExecutionScopeId
            || current.TeamId != request.TeamId || current.RuntimeProvider != request.Provider || current.AccessMode != RuntimeAccessMode.WsrxOnly))
            return new(ExecutionIsolationStatus.Unverified);
        if (current?.State == RuntimeState.Running)
        {
            if (current.ProviderReceipt is not ContainerRuntimeReceipt { IsolationState: RuntimeIsolationState.Verified } receipt
                || receipt.ExecutionScopeId != request.ExecutionScopeId || receipt.OperationId != current.Id
                || receipt.Provider != request.Provider || receipt.Services.Any(x => x.PublishedPorts.Count != 0))
                return new(ExecutionIsolationStatus.Unverified);
        }
        try
        {
            if (current?.RunnerId is { } runnerId)
            {
                var runner = await runners.ReadEligibleRunnerAsync(target.RunnerPool, runnerId, ct);
                return new(runner?.Provider == request.Provider && runner.ExecutionIsolation == RuntimeIsolationState.Verified
                    ? ExecutionIsolationStatus.Verified : ExecutionIsolationStatus.Unverified);
            }
            return new((await runners.ReadEligibleAsync(target.RunnerPool, ct)).Any(x => x.Provider == request.Provider
                && x.ExecutionIsolation == RuntimeIsolationState.Verified) ? ExecutionIsolationStatus.Verified : ExecutionIsolationStatus.Unverified);
        }
        catch (NatsException) { return new(ExecutionIsolationStatus.Unavailable); }
    }
}
