using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Rounds;

/// <summary>Union-based pause accounting prevents overlapping Match and competition pauses being deducted twice.</summary>
public static class LiveSoloActiveClock
{
    public static TimeSpan Elapsed(DateTimeOffset startedAt, DateTimeOffset now, IEnumerable<LiveSoloPauseInterval> pauses)
    {
        if (now <= startedAt) return TimeSpan.Zero;
        var intervals = pauses.Select(x => (Start: x.StartedAt > startedAt ? x.StartedAt : startedAt,
                End: x.EndedAt is { } end && end < now ? end : now))
            .Where(x => x.End > x.Start).OrderBy(x => x.Start).ThenBy(x => x.End).ToArray();
        var deducted = TimeSpan.Zero;
        DateTimeOffset? begin = null, finish = null;
        foreach (var interval in intervals)
        {
            if (begin is null) { begin = interval.Start; finish = interval.End; }
            else if (interval.Start <= finish) { if (interval.End > finish) finish = interval.End; }
            else { deducted += finish!.Value - begin.Value; begin = interval.Start; finish = interval.End; }
        }
        if (begin is not null) deducted += finish!.Value - begin.Value;
        return now - startedAt - deducted;
    }

    public static bool Paused(IEnumerable<LiveSoloPauseInterval> pauses) => pauses.Any(x => x.EndedAt is null);
}
