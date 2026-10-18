using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Composition;

public enum RunnerProviderFailureKind
{
    ProvisionRejected,
    ProvisionTimedOut,
    CleanupFailed,
    AvailabilityProbeFailed
}

public sealed class RunnerProviderHealthState(
    IOptions<RunnerOptions> options,
    TimeProvider timeProvider,
    ILogger<RunnerProviderHealthState> logger)
{
    private readonly object sync = new();
    private readonly Dictionary<RuntimeProvider, ProviderFailure> failures = [];

    public void ReportFailure(
        RuntimeProvider provider,
        RunnerProviderFailureKind failureKind,
        Guid runtimeInstanceId)
    {
        logger.LogWarning(
            "Runtime resource operation failed. Provider {Provider} FailureCode {FailureCode} RuntimeInstanceId {RuntimeInstanceId}",
            provider, failureKind, runtimeInstanceId);
    }

    public void ReportUnavailable(RuntimeProvider provider)
    {
        var now = timeProvider.GetUtcNow();
        ProviderFailure failure;
        lock (sync)
        {
            var count = failures.TryGetValue(provider, out var previous)
                ? checked(previous.ConsecutiveFailures + 1)
                : 1;
            failure = new(
                RunnerProviderFailureKind.AvailabilityProbeFailed,
                count,
                now.AddSeconds(options.Value.ProviderFailureHoldSeconds));
            failures[provider] = failure;
        }

        logger.LogWarning(
            "Runtime provider availability probe failed. Provider {Provider} ConsecutiveFailures {ConsecutiveFailures} UnavailableUntil {UnavailableUntil}",
            provider,
            failure.ConsecutiveFailures,
            failure.UnavailableUntil);
    }

    public void ReportSuccess(RuntimeProvider provider)
    {
        lock (sync)
            failures.Remove(provider);
    }

    public bool IsReady(RuntimeProvider provider) =>
        !TryGetActiveFailure(provider, out _);

    private bool TryGetActiveFailure(
        RuntimeProvider provider,
        out ProviderFailure? failure)
    {
        lock (sync)
        {
            if (!failures.TryGetValue(provider, out failure))
                return false;
            if (failure.UnavailableUntil <= timeProvider.GetUtcNow())
            {
                failures.Remove(provider);
                failure = null;
                return false;
            }
            return true;
        }
    }

    private sealed record ProviderFailure(
        RunnerProviderFailureKind FailureKind,
        int ConsecutiveFailures,
        DateTimeOffset UnavailableUntil);
}
