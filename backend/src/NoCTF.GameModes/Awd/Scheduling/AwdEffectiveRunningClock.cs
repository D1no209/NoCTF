using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Scheduling;

/// <summary>Reconstructs the pause-aware competition clock from immutable lifecycle facts.</summary>
public static class AwdEffectiveRunningClock
{
    public static TimeSpan Calculate(
        IEnumerable<CompetitionLifecycleAudit> audits,
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
