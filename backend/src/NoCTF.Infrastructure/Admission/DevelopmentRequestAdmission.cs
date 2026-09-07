using NoCTF.Application.Admission;

namespace NoCTF.Infrastructure.Admission;

/// <summary>Explicit single-process development adapter, never a Redis failure fallback.</summary>
public sealed class DevelopmentRequestAdmission(TimeProvider clock) : IRequestAdmission
{
    private readonly object sync = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset End)> counters = [];
    private readonly Dictionary<string, int> active = [];
    public ValueTask<IRequestAdmissionLease> AcquireAsync(IReadOnlyList<RateQuota> rates, IReadOnlyList<ConcurrentQuota> concurrency, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in counters.Where(item => item.Value.End <= now).Select(item => item.Key).ToArray()) counters.Remove(expired);
            foreach (var rate in rates)
                if (counters.TryGetValue(rate.Key, out var count) && count.Count >= rate.Limit)
                    throw new AdmissionRejectedException(AdmissionFailure.RateLimited, Math.Max(1, (int)Math.Ceiling((count.End - now).TotalSeconds)));
            foreach (var slot in concurrency)
                if (active.GetValueOrDefault(slot.Key) >= slot.Limit) throw new AdmissionRejectedException(AdmissionFailure.CapacityBusy);
            foreach (var rate in rates)
            { var count = counters.GetValueOrDefault(rate.Key, (0, now.AddSeconds(rate.WindowSeconds))); counters[rate.Key] = (count.Item1 + 1, count.Item2); }
            foreach (var slot in concurrency) active[slot.Key] = active.GetValueOrDefault(slot.Key) + 1;
            return ValueTask.FromResult<IRequestAdmissionLease>(new Lease(this, concurrency.Select(slot => slot.Key).ToArray(), ct));
        }
    }
    private sealed class Lease(DevelopmentRequestAdmission owner, string[] keys, CancellationToken ct) : IRequestAdmissionLease
    {
        private bool disposed;
        public CancellationToken Token => ct;
        public ValueTask DisposeAsync()
        { lock (owner.sync) { if (!disposed) { foreach (var key in keys) owner.active[key]--; disposed = true; } } return ValueTask.CompletedTask; }
    }
}
