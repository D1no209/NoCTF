using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Scheduling;

/// <summary>Reconstructs the pause-aware competition clock from immutable lifecycle facts.</summary>
public static class AwdEffectiveRunningClock
{
    internal static AwdEffectiveRunningTimeline CreateTimeline(
        IEnumerable<CompetitionLifecycleTransition> audits) => new(audits);

    /// <summary>Maps an effective boundary to wall time, clamping at a closed final segment.</summary>
    public static DateTimeOffset ToWallTime(
        IEnumerable<CompetitionLifecycleTransition> audits,
        DateTimeOffset start,
        TimeSpan target) => CreateTimeline(audits).ToWallTime(start, target);

    public static TimeSpan Calculate(
        IEnumerable<CompetitionLifecycleTransition> audits,
        DateTimeOffset at) => CreateTimeline(audits).Calculate(at);
}

internal sealed class AwdEffectiveRunningTimeline
{
    private readonly CompetitionLifecycleTransition[] transitions;

    public AwdEffectiveRunningTimeline(IEnumerable<CompetitionLifecycleTransition> audits)
    {
        transitions = audits.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id).ToArray();
    }

    public bool HasTransitions => transitions.Length > 0;

    public DateTimeOffset ToWallTime(DateTimeOffset start, TimeSpan target)
    {
        if (transitions.Length == 0)
            return start + target;
        var accumulated = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        foreach (var transition in transitions)
        {
            if (transition.To == CompetitionStatus.Running && runningSince is null)
                runningSince = transition.OccurredAt;
            else if (transition.From == CompetitionStatus.Running
                     && transition.To != CompetitionStatus.Running
                     && runningSince is DateTimeOffset segmentStart)
            {
                var segment = transition.OccurredAt - segmentStart;
                if (accumulated + segment >= target)
                    return segmentStart + (target - accumulated);
                accumulated += segment;
                runningSince = null;
            }
        }
        // A partial final round ends when play stops, never back at StartAt.
        return runningSince is { } current
            ? current + (target - accumulated)
            : transitions[^1].OccurredAt;
    }

    public TimeSpan Calculate(DateTimeOffset at)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        foreach (var audit in transitions)
        {
            if (audit.OccurredAt > at)
                break;
            if (audit.To == CompetitionStatus.Running && runningSince is null)
            {
                runningSince = audit.OccurredAt;
                continue;
            }

            if (audit.From == CompetitionStatus.Running
                && audit.To != CompetitionStatus.Running
                && runningSince is DateTimeOffset started)
            {
                total += audit.OccurredAt - started;
                runningSince = null;
            }
        }

        if (runningSince is DateTimeOffset current)
            total += at - current;
        return total;
    }
}
