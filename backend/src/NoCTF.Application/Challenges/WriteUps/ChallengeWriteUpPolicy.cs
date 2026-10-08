using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.WriteUps;

public static class ChallengeWriteUpPolicy
{
    public const int MaximumMarkdownCharacters = 262144;
    public const int MaximumDeadlineHours = 8760;
    public static bool CanSubmit(bool enabled, DateTimeOffset endAt, int deadlineHours, DateTimeOffset now) =>
        enabled && deadlineHours is >= 0 and <= MaximumDeadlineHours
        && now <= CompetitionWriteUpPolicy.DeadlineAt(endAt, deadlineHours);
    public static bool ValidContent(WriteUpFormat format, string? markdown, Guid? fileId) => format switch
    {
        WriteUpFormat.Markdown => fileId is null && !string.IsNullOrWhiteSpace(markdown)
            && markdown.Length <= MaximumMarkdownCharacters,
        WriteUpFormat.Pdf => fileId is not null && markdown is null,
        _ => false
    };
    public static long Deduction(long positivePoints, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(positivePoints);
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return checked((long)decimal.Ceiling((decimal)positivePoints * percent / 100m));
    }
}
