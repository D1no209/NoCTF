using NoCTF.Hosting.Health;
using NoCTF.Infrastructure.Messaging;
using Wolverine.Runtime;

namespace NoCTF.Worker;

public sealed class ClusterSchedulingReadinessDependency(
    IWolverineRuntime runtime,
    ClusterSchedulingState state,
    ClusterSchedulerNodeIdentity nodeIdentity,
    IClusterSchedulerStatusStore statusStore,
    TimeProvider timeProvider) : IReadinessDependency
{
    private static readonly TimeSpan MaximumRebuildAge = TimeSpan.FromSeconds(20);
    private IReadOnlyDictionary<string, object> diagnostics =
        new Dictionary<string, object>();

    public string Name => "cluster-scheduler";
    public bool FailureIsCritical => true;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        runtime.AssertHasStarted();
        var scheduler = await statusStore.ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No active cluster scheduler diagnostic lease was found.");
        var local = state.Read();
        diagnostics = new Dictionary<string, object>
        {
            ["localNode"] = nodeIdentity.Value,
            ["schedulerOwnerNode"] = scheduler.OwnerNode,
            ["schedulerTakenOverAt"] = scheduler.TakenOverAt,
            ["schedulerIsLocal"] = scheduler.OwnerNode == nodeIdentity.Value
        };

        if (scheduler.OwnerNode != nodeIdentity.Value)
        {
            if (local.IsActive)
            {
                throw new InvalidOperationException(
                    "The local scheduler is active but does not own the diagnostic lease.");
            }
            return;
        }

        if (!local.IsActive
            || local.LastTakenOverAt != scheduler.TakenOverAt
            || local.LastRebuiltAt is not { } rebuiltAt
            || timeProvider.GetUtcNow() - rebuiltAt > MaximumRebuildAge
            || local.LastError is not null)
        {
            throw new InvalidOperationException(
                "The local cluster scheduler has not restored a healthy schedule.");
        }
    }

    public IReadOnlyDictionary<string, object> Describe() => diagnostics;
}
