namespace NoCTF.Application.Competitions.Koh;

public static class KohPollSchedule
{
    public static DateTimeOffset NextDue(
        DateTimeOffset previousDue,
        DateTimeOffset completedAt,
        TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        if (previousDue > completedAt)
            return previousDue;
        var elapsedTicks = checked((completedAt - previousDue).Ticks);
        var intervals = checked(elapsedTicks / interval.Ticks + 1);
        return previousDue.AddTicks(checked(interval.Ticks * intervals));
    }
}
