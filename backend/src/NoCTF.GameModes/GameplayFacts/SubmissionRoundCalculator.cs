namespace NoCTF.GameModes.GameplayFact;

public static class GameplayFactRoundCalculator
{
    public static long Calculate(DateTimeOffset receivedAt, DateTimeOffset competitionStart, int roundDurationSeconds)
    {
        if (roundDurationSeconds <= 0)
            return 1;

        var elapsed = receivedAt - competitionStart;
        if (elapsed <= TimeSpan.Zero)
            return 1;

        var roundTicks = checked((long)roundDurationSeconds * TimeSpan.TicksPerSecond);
        return elapsed.Ticks / roundTicks + 1;
    }
}
