using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Persistence;

internal sealed class CriticalSectionTimeoutException(string scope)
    : TimeoutException($"The {scope} critical section was busy for more than two seconds.");

internal static class CriticalSectionCoordinator
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryLeases = new();
    private static readonly Meter Meter = new("NoCTF.Infrastructure.CriticalSections");
    private static readonly Histogram<double> WaitDuration = Meter.CreateHistogram<double>(
        "noctf.critical_section.wait.duration",
        "ms");
    private static readonly Counter<long> Timeouts = Meter.CreateCounter<long>(
        "noctf.critical_section.timeout");

    public static async ValueTask<IAsyncDisposable> AcquireAsync(
        NoCtfDbContext db,
        string scope,
        Func<CancellationToken, Task<int>> acquireRelationalRow,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(WaitBudget);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            IAsyncDisposable lease;
            if (db.Database.IsRelational())
            {
                var affected = await acquireRelationalRow(budget.Token);
                if (affected != 1)
                    throw new DbUpdateConcurrencyException($"The {scope} anchor no longer exists.");
                lease = NoopLease.Instance;
            }
            else
            {
                var gate = InMemoryLeases.GetOrAdd(scope, static _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(budget.Token);
                lease = new SemaphoreLease(gate);
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            WaitDuration.Record(
                elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", scope));
            if (elapsed > SlowThreshold)
            {
                db.GetService<ILoggerFactory>()
                    .CreateLogger("NoCTF.CriticalSection")
                    .LogWarning(
                        "Critical section {Scope} took {ElapsedMilliseconds:F1} ms to acquire",
                        scope,
                        elapsed.TotalMilliseconds);
            }
            return lease;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Timeouts.Add(1, new KeyValuePair<string, object?>("scope", scope));
            throw new CriticalSectionTimeoutException(scope);
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SemaphoreLease(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class CriticalSectionLeaseCollection : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> leases = [];

    public void Add(IAsyncDisposable lease) => leases.Add(lease);

    public async ValueTask DisposeAsync()
    {
        for (var index = leases.Count - 1; index >= 0; index--)
            await leases[index].DisposeAsync();
    }
}
