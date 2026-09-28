using System.Collections.Concurrent;

namespace NoCTF.Infrastructure.Messaging;

/// <summary>
/// Process-local merge window drained by the Worker instance that receives a
/// JetStream leaderboard event.
/// </summary>
public sealed class LeaderboardProjectionMergeQueue
{
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> dueAt = new();

    public bool Enqueue(Guid competitionId, DateTimeOffset observedAt)
    {
        var due = observedAt.Add(MergeWindow);
        return dueAt.TryAdd(competitionId, due);
    }

    public IReadOnlyList<Guid> TakeDue(DateTimeOffset now)
    {
        var due = dueAt
            .Where(pair => pair.Value <= now)
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => pair.Key)
            .ToArray();
        if (due.Length == 0)
            return due;

        var taken = new List<Guid>(due.Length);
        foreach (var competitionId in due)
        {
            if (dueAt.TryRemove(competitionId, out _))
                taken.Add(competitionId);
        }
        return taken;
    }

    public void Retry(Guid competitionId, DateTimeOffset due)
    {
        dueAt.AddOrUpdate(
            competitionId,
            due,
            (_, current) => current <= due ? current : due);
    }

    internal int Count => dueAt.Count;
}
