namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RunnerCapacityLedgerCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? value = gate;

        public void Dispose() => Interlocked.Exchange(ref value, null)?.Release();
    }
}
