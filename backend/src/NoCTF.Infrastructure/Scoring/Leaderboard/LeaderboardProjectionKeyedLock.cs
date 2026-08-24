using System.Collections.Concurrent;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class LeaderboardProjectionKeyedLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    public async ValueTask<IDisposable> EnterAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var gate = locks.GetOrAdd(competitionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                gate.Release();
        }
    }
}
