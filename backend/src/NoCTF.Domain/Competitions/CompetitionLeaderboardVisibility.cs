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
        DateTimeOffset? frozenStartAt,
        DateTimeOffset? hiddenStartAt,
        DateTimeOffset now)
    {
        var frozenEffective = frozenStartAt is { } frozen && frozen <= now;
        var hiddenEffective = hiddenStartAt is { } hidden && hidden <= now;

        if (!frozenEffective && !hiddenEffective)
            return CompetitionLeaderboardVisibility.Normal;
        if (!hiddenEffective)
            return CompetitionLeaderboardVisibility.Frozen;
        if (!frozenEffective)
            return CompetitionLeaderboardVisibility.Blackout;

        return hiddenStartAt >= frozenStartAt
            ? CompetitionLeaderboardVisibility.Blackout
            : CompetitionLeaderboardVisibility.Frozen;
    }
}
