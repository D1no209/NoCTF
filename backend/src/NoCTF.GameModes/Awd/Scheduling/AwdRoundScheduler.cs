namespace NoCTF.GameModes.Awd.Scheduling;

/// <summary>Bounded retry timing for AWD flag delivery.</summary>
public static class AwdRoundScheduler
{
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
