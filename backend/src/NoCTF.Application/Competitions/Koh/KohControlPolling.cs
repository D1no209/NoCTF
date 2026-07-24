namespace NoCTF.Application.Competitions.Koh;

public enum KohControlResponseKind
{
    Success,
    Timeout,
    Unavailable
}

public sealed record KohControlResponse(
    KohControlResponseKind Kind,
    ReadOnlyMemory<byte> Body)
{
    public static KohControlResponse Success(ReadOnlyMemory<byte> body) =>
        new(KohControlResponseKind.Success, body);

    public static KohControlResponse Timeout() =>
        new(KohControlResponseKind.Timeout, ReadOnlyMemory<byte>.Empty);

    public static KohControlResponse Unavailable() =>
        new(KohControlResponseKind.Unavailable, ReadOnlyMemory<byte>.Empty);
}

public interface IKohControlClient
{
    Task<KohControlResponse> ObserveAsync(
        Uri controlUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

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
