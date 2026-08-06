namespace NoCTF.Infrastructure.Persistence;

internal sealed class FeatureCriticalSectionTimeoutException(string feature)
    : TimeoutException($"The {feature} critical section was busy for more than two seconds.");

public sealed class LocalCriticalSectionRegistry
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(2);
    private readonly Lock sync = new();
    private readonly Dictionary<string, Entry> entries = [];

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string feature,
        string key,
        CancellationToken cancellationToken)
    {
        Entry entry;
        lock (sync)
        {
            if (!entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                entries.Add(key, entry);
            }
            entry.References++;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(WaitBudget);
        try
        {
            await entry.Gate.WaitAsync(budget.Token);
            return new Lease(this, key, entry);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ReleaseReference(key, entry);
            throw new FeatureCriticalSectionTimeoutException(feature);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(string key, Entry entry)
    {
        entry.Gate.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, Entry entry)
    {
        lock (sync)
        {
            entry.References--;
            if (entry.References == 0)
                entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Lease(
        LocalCriticalSectionRegistry owner,
        string key,
        Entry entry) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Release(key, entry);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class NoopCriticalSectionLease : IAsyncDisposable
{
    public static readonly NoopCriticalSectionLease Instance = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
