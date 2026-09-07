using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Scheduling;

/// <summary>Reconstructs the pause-aware competition clock from immutable lifecycle facts.</summary>
public static class AwdEffectiveRunningClock
{
    /// <summary>Maps an effective boundary to wall time, clamping at a closed final segment.</summary>
    public static DateTimeOffset ToWallTime(
        IEnumerable<CompetitionLifecycleTransition> audits,
        DateTimeOffset start,
        TimeSpan target)
    {
        var transitions = audits.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id).ToArray();
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

    public static TimeSpan Calculate(
        IEnumerable<CompetitionLifecycleTransition> audits,
        DateTimeOffset at)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? runningSince = null;
        foreach (var audit in audits
                     .Where(audit => audit.OccurredAt <= at)
                     .OrderBy(audit => audit.OccurredAt)
                     .ThenBy(audit => audit.Id))
        {
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
