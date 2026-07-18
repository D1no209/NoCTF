using NoCTF.GameModes.Awdp.Configuration;

namespace NoCTF.GameModes.Awdp.Scoring;

public static class AwdpScoringRules
{
    public static bool ConsumesAttempt(bool teamControlledFailure, bool platformFailure) =>
        teamControlledFailure && !platformFailure;

    public static long ScoreForSuccess(AwdpAchievementConfiguration configuration) => configuration.Points;
}
