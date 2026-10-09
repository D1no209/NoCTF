namespace NoCTF.Application.LiveSolo.Media;

public static class LiveSoloProgramPolicy
{
    public static DateTimeOffset PublicationTime(DateTimeOffset videoEnd, DateTimeOffset firstObserved, int delaySeconds)
    {
        if (delaySeconds is < 0 or > 86400) throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        return (videoEnd > firstObserved ? videoEnd : firstObserved).AddSeconds(delaySeconds);
    }
    public static bool MayReadSegment(DateTimeOffset publicAt, DateTimeOffset removeAfter, DateTimeOffset now) => publicAt <= now && now < removeAfter;
}
