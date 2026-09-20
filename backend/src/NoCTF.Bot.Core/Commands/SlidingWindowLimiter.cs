using System.Collections.Concurrent;

namespace NoCTF.Bot.Commands;

public sealed class SlidingWindowLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> windows = new();

    public bool TryAcquire(string key, int limit, TimeSpan window)
    {
        var now = timeProvider.GetUtcNow();
        var timestamps = windows.GetOrAdd(key, static _ => new());
        lock (timestamps)
        {
            while (timestamps.TryPeek(out var timestamp) && now - timestamp >= window)
                timestamps.Dequeue();
            if (timestamps.Count >= limit) return false;
            timestamps.Enqueue(now);
            return true;
        }
    }
}
