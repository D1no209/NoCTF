namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class LeaderboardProjectionKeyedLock : IDisposable
{
    private readonly AsyncKeyedLock.AsyncKeyedLocker<Guid> locks = new();

    public async ValueTask<IDisposable> EnterAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        return await locks.LockAsync(competitionId, cancellationToken);
    }

    public void Dispose() => locks.Dispose();
}
