using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NoCTF.Tests.Fixtures;

internal sealed class CompetitionLeaderboardUpdateBarrier(int expectedArrivals = 2)
    : DbCommandInterceptor
{
    private readonly TaskCompletionSource<bool> release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    private int enabled;

    public int Arrivals => Volatile.Read(ref arrivals);

    public void Enable() => Volatile.Write(ref enabled, 1);

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await WaitForConcurrentUpdateAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await WaitForConcurrentUpdateAsync(command, cancellationToken);
        return result;
    }

    private async ValueTask WaitForConcurrentUpdateAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref enabled) == 0
            || !command.CommandText.Split(';').Any(IsLeaderboardUpdate))
            return;

        if (Interlocked.Increment(ref arrivals) == expectedArrivals)
            release.TrySetResult(true);
        await release.Task.WaitAsync(cancellationToken);
    }

    private static bool IsLeaderboardUpdate(string statement)
    {
        var sql = statement.TrimStart();
        return sql.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("competitions", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("leaderboard_revision", StringComparison.OrdinalIgnoreCase);
    }
}
