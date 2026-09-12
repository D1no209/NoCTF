namespace NoCTF.Domain.Competitions;

public static class CompetitionWriteUpPolicy
{
    public const int MaximumDeadlineHours = 24 * 365;

    public static bool IsDeadlineHoursValid(int value) =>
        value is >= 0 and <= MaximumDeadlineHours;

    public static DateTimeOffset DeadlineAt(
        DateTimeOffset competitionEndAt,
        int deadlineHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deadlineHours);
        var extension = TimeSpan.FromHours(deadlineHours);
        return competitionEndAt > DateTimeOffset.MaxValue - extension
            ? DateTimeOffset.MaxValue
            : competitionEndAt + extension;
    }

    public static bool CanSubmit(
        bool submissionRequired,
        DateTimeOffset competitionEndAt,
        int deadlineHours,
        DateTimeOffset now) =>
        submissionRequired
        && IsDeadlineHoursValid(deadlineHours)
        && now <= DeadlineAt(competitionEndAt, deadlineHours);
}
