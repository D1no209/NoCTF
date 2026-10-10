using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Access;

public sealed class RuntimeProxyTargetReader(
    NoCtfDbContext db, IExecutionRuntimeIsolation? isolation = null) : IRuntimeProxyTargetReader
{
    public async Task<RuntimeProxyTarget?> FindAsync(
        Guid runtimeInstanceId,
        int bindingIndex,
        CancellationToken cancellationToken)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == runtimeInstanceId
                && runtime.State == RuntimeState.Running
                && (runtime.AccessMode == RuntimeAccessMode.DirectAndWsrx
                    || runtime.AccessMode == RuntimeAccessMode.WsrxOnly))
            .SingleOrDefaultAsync(cancellationToken);
        if (runtime?.ExecutionScopeId is Guid scope && (runtime.TeamId is not Guid team || isolation is null
            || (await isolation.AssessAsync(new(runtime.RuntimeKind, runtime.RuntimeProvider, scope, team, runtime.Id), cancellationToken)).Status
                != ExecutionIsolationStatus.Verified)) return null;
        var endpoint = runtime?.AccessEndpoints.SingleOrDefault(candidate =>
            candidate.BindingIndex == bindingIndex
            && !string.IsNullOrWhiteSpace(candidate.TargetHost)
            && candidate.TargetPort is >= 1 and <= 65535);
        return runtime is null || endpoint is null
            ? null
            : new RuntimeProxyTarget(
                runtime.Id,
                endpoint.BindingIndex,
                endpoint.TargetHost!,
                endpoint.TargetPort!.Value,
                runtime.CompetitionId,
                runtime.CompetitionChallengeId,
                runtime.TeamId,
                runtime.TrafficCaptureEnabled,
                runtime.TrafficCaptureLimitBytes) { ExecutionScopeId = runtime.ExecutionScopeId };
    }
}
