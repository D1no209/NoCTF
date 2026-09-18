namespace NoCTF.Infrastructure.Runtime.Capacity;

/// <summary>Excludes provider mutations while inventory is read; this is not a business queue.</summary>
public sealed class RunnerResourceMutationCoordinator : IDisposable
{
    private const int Slots = 128;
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly SemaphoreSlim mutations = new(Slots, Slots);

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await admission.WaitAsync(ct);
        try { await mutations.WaitAsync(ct); }
        finally { admission.Release(); }
        return new Lease(() => mutations.Release());
    }

    public async Task<IDisposable> ReconcileAsync(CancellationToken ct)
    {
        await admission.WaitAsync(ct);
        var acquired = 0;
        try
        {
            for (; acquired < Slots; acquired++)
                await mutations.WaitAsync(ct);
            return new Lease(() => { mutations.Release(Slots); admission.Release(); });
        }
        catch
        {
            if (acquired > 0) mutations.Release(acquired);
            admission.Release();
            throw;
        }
    }

    public void Dispose() { admission.Dispose(); mutations.Dispose(); }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
