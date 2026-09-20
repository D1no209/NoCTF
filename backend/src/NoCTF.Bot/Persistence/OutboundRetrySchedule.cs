namespace NoCTF.Bot.Persistence;

internal static class OutboundRetrySchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    ];

    public static TimeSpan? AfterFailure(int attemptCount) =>
        attemptCount <= 0 || attemptCount > Delays.Length
            ? null
            : Delays[attemptCount - 1];
}
