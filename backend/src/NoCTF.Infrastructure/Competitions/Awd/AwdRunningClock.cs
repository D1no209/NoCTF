using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

/// <summary>Maps elapsed running time back to its actual observation time, including pauses
/// inside a round. Future positions assume the current running interval continues.</summary>
internal sealed class AwdRunningClock(IReadOnlyList<(DateTimeOffset Start, DateTimeOffset? End)> segments)
{
    public TimeSpan ElapsedAt(DateTimeOffset at) => TimeSpan.FromTicks(segments.Sum(segment =>
    {
        var end = segment.End is { } stopped && stopped < at ? stopped : at;
        return end > segment.Start ? (end - segment.Start).Ticks : 0;
    }));

    public DateTimeOffset At(TimeSpan position, bool intervalStart)
    {
        var remaining = position;
        foreach (var segment in segments)
        {
            if (segment.End is not { } end)
                return segment.Start + remaining;
            var length = end - segment.Start;
            if (remaining < length || (!intervalStart && remaining == length))
                return segment.Start + remaining;
            remaining -= length;
        }
        throw new InvalidOperationException("An AWD running position has no lifecycle interval.");
    }

    public static async Task<AwdRunningClock> ReadAsync(NoCtfDbContext db, Guid competitionId,
        DateTimeOffset competitionStart, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var events = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged && item.OccurredAt <= observedAt)
            .OrderBy(item => item.OccurredAt).ThenBy(item => item.Id)
            .Select(item => new { item.OccurredAt, item.CompetitionStatus }).ToArrayAsync(cancellationToken);
        var segments = new List<(DateTimeOffset Start, DateTimeOffset? End)>();
        DateTimeOffset? start = null;
        foreach (var item in events)
        {
            var at = item.OccurredAt < competitionStart ? competitionStart : item.OccurredAt;
            if (item.CompetitionStatus == CompetitionStatus.Running)
                start ??= at;
            else if (start is { } started)
            {
                segments.Add((started, at));
                start = null;
            }
        }
        if (start is { } current)
            segments.Add((current, null));
        return new(segments);
    }
}
