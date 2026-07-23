namespace NoCTF.GameModes.Awd.Scheduling;

using NoCTF.Domain.Challenges;

/// <summary>Pure AWD clock and injection retry rules shared by scheduling handlers.</summary>
public static class AwdRoundScheduler
{
    public static AwdRoundWindow? ResolveInitialRound(
        TimeSpan effectiveRunningTime,
        TimeSpan hardeningDuration,
        TimeSpan roundDuration)
    {
        if (effectiveRunningTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(effectiveRunningTime));
        if (hardeningDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(hardeningDuration));
        if (roundDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(roundDuration));
        if (effectiveRunningTime < hardeningDuration)
            return null;

        var elapsed = effectiveRunningTime - hardeningDuration;
        var roundNumber = checked(elapsed.Ticks / roundDuration.Ticks + 1);
        if (roundNumber > AwdRoundSpecificationId.MaximumRound)
            return null;
        var round = checked((int)roundNumber);
        var start = hardeningDuration + TimeSpan.FromTicks(checked(roundDuration.Ticks * (round - 1L)));
        return new AwdRoundWindow(round, start, start + roundDuration);
    }

    public static bool IsWithinWindow(AwdRoundWindow window, TimeSpan effectiveRunningTime) =>
        effectiveRunningTime >= window.StartOffset && effectiveRunningTime < window.EndOffset;

    /// <summary>
    /// Resolves a durable-timeline delivery. <paramref name="dueAt"/> must already be
    /// translated by the caller onto the pause-adjusted logical timeline; a raw resume
    /// wall-clock timestamp is not a valid input.
    /// </summary>
    public static AwdPersistedRoundWindow ResolvePersistedTimelineRound(
        int latestRound,
        DateTimeOffset latestStart,
        DateTimeOffset latestEnd,
        DateTimeOffset dueAt,
        TimeSpan roundDuration)
    {
        _ = AwdRoundSpecificationId.FromRound(latestRound);
        if (latestStart >= latestEnd)
            throw new ArgumentOutOfRangeException(nameof(latestEnd));
        if (roundDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(roundDuration));
        if (dueAt < latestEnd)
            return new(latestRound, latestStart, latestEnd);

        var nextRound = checked(latestRound + 1);
        _ = AwdRoundSpecificationId.FromRound(nextRound);
        var missedWindows = (dueAt - latestEnd).Ticks / roundDuration.Ticks;
        var skippedTicks = checked(missedWindows * roundDuration.Ticks);
        var start = latestEnd.AddTicks(skippedTicks);
        return new(nextRound, start, start + roundDuration);
    }

    public static DateTimeOffset? NextInjectionAttempt(
        DateTimeOffset now,
        DateTimeOffset validUntil,
        int failedAttempts)
    {
        if (failedAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(failedAttempts));
        if (now >= validUntil)
            return null;

        var delaySeconds = failedAttempts >= 5
            ? 30L
            : 1L << failedAttempts;
        var remaining = validUntil - now;
        if (TimeSpan.FromSeconds(delaySeconds) >= remaining)
            return null;
        return now.AddSeconds(delaySeconds);
    }
}

public readonly record struct AwdRoundWindow(
    int Round,
    TimeSpan StartOffset,
    TimeSpan EndOffset);

public readonly record struct AwdPersistedRoundWindow(
    int Round,
    DateTimeOffset ValidStart,
    DateTimeOffset ValidUntil);
