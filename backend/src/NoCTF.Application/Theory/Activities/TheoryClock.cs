namespace NoCTF.Application.Theory.Activities;

public sealed record TheoryPauseInterval(DateTimeOffset Start, DateTimeOffset? End);
public sealed record TheoryDeadline(DateTimeOffset At, TimeSpan Extension, bool Paused, bool Capped);

/// <summary>Derives deadlines from durable pause facts; neither browser time nor delivery time is authoritative.</summary>
public static class TheoryClock
{
    public static TheoryDeadline Calculate(DateTimeOffset anchor, DateTimeOffset baseDeadline,
        IReadOnlyCollection<TheoryPauseInterval> pauses, DateTimeOffset now, DateTimeOffset? hardEnd = null)
    {
        if (baseDeadline < anchor) throw new ArgumentOutOfRangeException(nameof(baseDeadline));
        if (pauses.Any(pause => pause.End < pause.Start))
            throw new ArgumentException("Pause end precedes its start.", nameof(pauses));
        var effectiveNow = hardEnd is { } cap && cap < now ? cap : now;
        var intervals = pauses.Where(pause => pause.Start <= effectiveNow && (pause.End is null || pause.End > anchor))
            .Select(pause => new Interval(pause.Start < anchor ? anchor : pause.Start,
                pause.End is { } end && end < effectiveNow ? end : effectiveNow))
            .Where(pause => pause.End >= pause.Start).OrderBy(pause => pause.Start).ToArray();
        var merged = new List<Interval>();
        foreach (var interval in intervals)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = interval.End > merged[^1].End ? interval.End : merged[^1].End };
            else merged.Add(interval);
        }
        var deadline = baseDeadline;
        foreach (var pause in merged)
        {
            // A pause starting at/after expiration cannot resurrect an expired activity or paper.
            if (pause.Start >= deadline) break;
            deadline += pause.End - pause.Start;
        }
        var extension = deadline - baseDeadline;
        var capped = hardEnd is { } limit && limit < deadline;
        if (capped) deadline = hardEnd!.Value;
        var paused = now < deadline && pauses.Any(pause => pause.Start <= now
            && (pause.End is null || pause.End > now));
        return new(deadline, extension, paused, capped);
    }

    public static TheoryDeadline Paper(DateTimeOffset openedAt, int durationSeconds,
        TheoryDeadline activityDeadline, IReadOnlyCollection<TheoryPauseInterval> pauses, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationSeconds, 1);
        return Calculate(openedAt, openedAt.AddSeconds(durationSeconds), pauses, now, activityDeadline.At);
    }

    private sealed record Interval(DateTimeOffset Start, DateTimeOffset End);
}
