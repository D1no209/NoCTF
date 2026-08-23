using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleMoment(
    Guid EventId,
    DateTimeOffset OccurredAt,
    CompetitionStatus From,
    CompetitionStatus To);

public readonly record struct CompetitionEffectiveRuntime(
    TimeSpan Elapsed,
    DateTimeOffset? RunningSince);

public static class CompetitionEffectiveRuntimePolicy
{
    public static CompetitionEffectiveRuntime Calculate(
        DateTimeOffset competitionStartAt,
        DateTimeOffset competitionEndAt,
        DateTimeOffset now,
        IEnumerable<CompetitionLifecycleMoment> transitions)
    {
        var elapsed = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        var effectiveEnd = now < competitionEndAt ? now : competitionEndAt;
        foreach (var transition in transitions
                     .Where(item => item.OccurredAt <= effectiveEnd)
                     .OrderBy(item => item.OccurredAt)
                     .ThenBy(item => item.EventId))
        {
            var occurredAt = Clamp(transition.OccurredAt, competitionStartAt, competitionEndAt);
            if (runningSince is { } startedAt && transition.To != CompetitionStatus.Running)
            {
                if (occurredAt > startedAt)
                    elapsed += occurredAt - startedAt;
                runningSince = null;
            }

            if (transition.To == CompetitionStatus.Running && runningSince is null)
                runningSince = occurredAt;
        }

        if (runningSince is { } currentStart && effectiveEnd > currentStart)
            elapsed += effectiveEnd - currentStart;

        return new(elapsed, runningSince is not null && now < competitionEndAt
            ? runningSince
            : null);
    }

    private static DateTimeOffset Clamp(
        DateTimeOffset value,
        DateTimeOffset minimum,
        DateTimeOffset maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
