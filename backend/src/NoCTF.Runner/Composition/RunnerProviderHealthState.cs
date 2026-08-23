using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Composition;

public enum RunnerProviderFailureKind
{
    ProvisionRejected,
    ProvisionTimedOut,
    CleanupFailed
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
        var now = timeProvider.GetUtcNow();
        ProviderFailure failure;
        lock (sync)
        {
            var count = failures.TryGetValue(provider, out var previous)
                ? checked(previous.ConsecutiveFailures + 1)
                : 1;
            failure = new(
                failureKind,
                runtimeInstanceId,
                count,
                now.AddSeconds(options.Value.ProviderFailureHoldSeconds));
            failures[provider] = failure;
        }

        logger.LogWarning(
            "Runtime provider unavailable after a resource operation failure. Provider {Provider} FailureCode {FailureCode} RuntimeInstanceId {RuntimeInstanceId} ConsecutiveFailures {ConsecutiveFailures} UnavailableUntil {UnavailableUntil}",
            provider,
            failureKind,
            runtimeInstanceId,
            failure.ConsecutiveFailures,
            failure.UnavailableUntil);
    }

    public void ReportSuccess(RuntimeProvider provider)
    {
        lock (sync)
            failures.Remove(provider);
    }

    public void EnsureReady(RuntimeProvider provider)
    {
        if (TryGetActiveFailure(provider, out var failure))
        {
            throw new InvalidOperationException(
                $"Runtime provider '{provider}' is cooling down after '{failure!.FailureKind}'.");
        }
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
        Guid RuntimeInstanceId,
        int ConsecutiveFailures,
        DateTimeOffset UnavailableUntil);
}
