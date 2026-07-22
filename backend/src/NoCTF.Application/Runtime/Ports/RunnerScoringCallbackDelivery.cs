namespace NoCTF.Application.Runtime.Ports;

public static class RunnerScoringCallbackDeliveryPolicy
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    public static readonly TimeSpan MaximumDeliveryBudget =
        TimeSpan.FromTicks(AttemptTimeout.Ticks * RetryDelays.Count + RetryDelays.Sum(delay => delay.Ticks));
    public static readonly TimeSpan OneShotCleanupBudget = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TransportResponseBudget = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DispatchLeaseBuffer =
        MaximumDeliveryBudget + OneShotCleanupBudget + TransportResponseBudget;
}

public interface IRunnerScoringCallbackDispatcher
{
    Task DispatchAsync(
        RunnerScoringCallback? callback,
        OneShotResult result,
        bool timedOut,
        CancellationToken cancellationToken);
}
