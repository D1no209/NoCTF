using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Hosting.Health;
using NoCTF.Infrastructure.Messaging;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;

namespace NoCTF.Worker;

public sealed class ClusterLeadershipStatusReporter(
    NodeAgentController agents,
    ClusterSchedulerNodeIdentity nodeIdentity,
    IClusterSchedulerStatusStore statusStore,
    TimeProvider timeProvider,
    ILogger<ClusterLeadershipStatusReporter> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (agents.IsLeader)
                {
                    await statusStore.ReportLeaderAsync(
                        new(nodeIdentity.Value, timeProvider.GetUtcNow()),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to report the active Wolverine leader diagnostic lease.");
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }
}

public sealed class ClusterSchedulingReadinessDependency(
    IWolverineRuntime runtime,
    NodeAgentController agents,
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
        if (!agents.HasStartedLocalAgentWorkflowForBalancedMode)
        {
            throw new InvalidOperationException(
                "Wolverine balanced agent assignment has not started.");
        }

        var scheduler = await statusStore.ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No active cluster scheduler diagnostic lease was found.");
        var leader = await statusStore.ReadLeaderAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No active Wolverine leader diagnostic lease was found.");
        var local = state.Read();
        diagnostics = new Dictionary<string, object>
        {
            ["leaderNode"] = leader.LeaderNode,
            ["localNode"] = nodeIdentity.Value,
            ["localNodeIsLeader"] = agents.IsLeader,
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
