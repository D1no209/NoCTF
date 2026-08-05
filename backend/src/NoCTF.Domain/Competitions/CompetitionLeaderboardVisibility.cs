namespace NoCTF.Domain.Competitions;

/// <summary>Controls which competitive facts participants may observe.</summary>
public enum CompetitionLeaderboardVisibility : short
{
    Normal,
    Frozen,
    Blackout
}

public static class CompetitionLeaderboardVisibilityPolicy
{
    public static CompetitionLeaderboardVisibility EffectiveAt(
        CompetitionStatus status,
        CompetitionLeaderboardVisibility configured,
        DateTimeOffset? startsAt,
        DateTimeOffset now)
    {
        if (status == CompetitionStatus.Finished
            || configured == CompetitionLeaderboardVisibility.Normal)
            return CompetitionLeaderboardVisibility.Normal;

        return startsAt is null || startsAt <= now
            ? configured
            : CompetitionLeaderboardVisibility.Normal;
    }
}
