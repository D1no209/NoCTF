using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Scheduling;

/// <summary>Round positions on the running clock. Pauses are removed before this policy;
/// gaps are removed here when calculating the flag/check active clock.</summary>
public static class AwdRoundTimeline
{
    public static AwdTimelinePlan Plan(
        IReadOnlyList<AwdRound> rounds,
        TimeSpan runningElapsed,
        TimeSpan hardening,
        TimeSpan nextRoundDuration,
        int nextGapSeconds,
        int? totalRounds = null,
        TimeSpan? activeTimeLimit = null)
    {
        if (runningElapsed < TimeSpan.Zero || hardening < TimeSpan.Zero
            || nextRoundDuration <= TimeSpan.Zero || nextGapSeconds < 0 || totalRounds is <= 0)
            throw new ArgumentOutOfRangeException(nameof(runningElapsed));
        var ordered = rounds.OrderBy(round => round.Number).ToArray();
        long precedingGapTicks = 0;
        foreach (var round in ordered)
        {
            var start = checked(round.ActiveStartTicks + precedingGapTicks);
            var end = checked(round.ActiveEndTicks + precedingGapTicks);
            if (runningElapsed.Ticks < start)
                return new(null, [], TimeSpan.FromTicks(start), round.ActiveStartTicks, false);
            if (runningElapsed.Ticks < end)
                return new(round, [], TimeSpan.FromTicks(end), runningElapsed.Ticks - precedingGapTicks, false);
            if (totalRounds is { } limit && round.Number >= limit)
                return new(null, [], null, round.ActiveEndTicks, false);
            if (activeTimeLimit is { } duration && round.ActiveEndTicks >= duration.Ticks)
                return new(null, [], null, round.ActiveEndTicks, false);
            var gapEnd = checked(end + TimeSpan.FromSeconds(round.GapSeconds).Ticks);
            if (runningElapsed.Ticks < gapEnd)
                return new(null, [], TimeSpan.FromTicks(gapEnd), round.ActiveEndTicks, true);
            precedingGapTicks = checked(precedingGapTicks + TimeSpan.FromSeconds(round.GapSeconds).Ticks);
        }

        var number = ordered.LastOrDefault()?.Number + 1 ?? 1;
        var activeStart = ordered.LastOrDefault()?.ActiveEndTicks ?? hardening.Ticks;
        var planned = new List<AwdPlannedRound>();
        while (totalRounds is null || number <= totalRounds)
        {
            if (activeTimeLimit is { } durationLimit && activeStart >= durationLimit.Ticks)
                return new(null, planned, null, activeStart, false);
            if (number > 1_000_000)
                break;
            var runningStart = checked(activeStart + precedingGapTicks);
            if (runningElapsed.Ticks < runningStart)
                return new(null, planned, TimeSpan.FromTicks(runningStart), activeStart, number > 1);
            var fullEnd = checked(activeStart + nextRoundDuration.Ticks);
            var activeEnd = activeTimeLimit is { } duration ? Math.Min(fullEnd, duration.Ticks) : fullEnd;
            var runningEnd = checked(activeEnd + precedingGapTicks);
            var missed = runningElapsed.Ticks >= runningEnd;
            planned.Add(new(number, activeStart, activeEnd, runningStart, runningEnd, nextGapSeconds, missed,
                activeEnd < fullEnd));
            if (!missed)
                return new(null, planned, TimeSpan.FromTicks(runningEnd), runningElapsed.Ticks - precedingGapTicks, false);
            if (totalRounds is { } limit && number >= limit)
                return new(null, planned, null, activeEnd, false);
            if (activeTimeLimit is { } finalDuration && activeEnd >= finalDuration.Ticks)
                return new(null, planned, null, activeEnd, false);
            var gapEnd = checked(runningEnd + TimeSpan.FromSeconds(nextGapSeconds).Ticks);
            if (runningElapsed.Ticks < gapEnd)
                return new(null, planned, TimeSpan.FromTicks(gapEnd), activeEnd, true);
            activeStart = activeEnd;
            precedingGapTicks = checked(precedingGapTicks + TimeSpan.FromSeconds(nextGapSeconds).Ticks);
            number++;
        }
        return new(null, planned, null, activeStart, false);
    }

    /// <summary>The stopping position excludes the final round's unused gap. Existing
    /// round lengths and gaps remain authoritative when settings change.</summary>
    public static TimeSpan EndPosition(IReadOnlyList<AwdRound> rounds, TimeSpan hardening,
        TimeSpan nextRoundDuration, int nextGapSeconds, int? totalRounds, TimeSpan activeTimeLimit)
    {
        long active = hardening.Ticks;
        long gaps = 0;
        var number = 0;
        if (totalRounds is null && activeTimeLimit.Ticks <= active)
            return activeTimeLimit;
        foreach (var round in rounds.OrderBy(round => round.Number))
        {
            active = round.ActiveEndTicks;
            number = round.Number;
            if (totalRounds is { } limit ? number >= limit : active >= activeTimeLimit.Ticks)
                return TimeSpan.FromTicks(checked(active + gaps));
            gaps = checked(gaps + TimeSpan.FromSeconds(round.GapSeconds).Ticks);
        }
        var remainingRounds = totalRounds is { } count ? Math.Max(0, count - number)
            : (int)Math.Max(0, Math.Ceiling((decimal)(activeTimeLimit.Ticks - active) / nextRoundDuration.Ticks));
        if (remainingRounds == 0) return TimeSpan.FromTicks(checked(active + gaps));
        var endActive = totalRounds is not null
            ? checked(active + remainingRounds * nextRoundDuration.Ticks) : activeTimeLimit.Ticks;
        return TimeSpan.FromTicks(checked(endActive + gaps
            + (remainingRounds - 1L) * TimeSpan.FromSeconds(nextGapSeconds).Ticks));
    }
}

public sealed record AwdPlannedRound(int Number, long ActiveStartTicks, long ActiveEndTicks,
    long RunningStartTicks, long RunningEndTicks, int GapSeconds, bool Missed, bool Partial = false);

public sealed record AwdTimelinePlan(AwdRound? Current, IReadOnlyList<AwdPlannedRound> NewRounds,
    TimeSpan? NextRunningPosition, long ActiveTicks, bool InGap);
