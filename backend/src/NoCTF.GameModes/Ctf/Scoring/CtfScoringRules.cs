using NoCTF.GameModes.Ctf.Configuration;

namespace NoCTF.GameModes.Ctf.Scoring;

public static class CtfScoringRules
{
    public static long CalculatePoints(CtfPointConfiguration points, int priorSolveCount)
    {
        var decayed = points.MinimumPoints
            + (points.InitialPoints - points.MinimumPoints)
            / (1m + priorSolveCount / points.DecayFactor);
        return decimal.ToInt64(decimal.Round(decayed, 0, MidpointRounding.AwayFromZero));
    }

    public static long CalculateBlood(
        BloodReward reward,
        CtfPointConfiguration points,
        long solveTimePoints) =>
        reward.Policy switch
        {
            BloodRewardPolicy.FixedPoints => decimal.ToInt64(decimal.Round(reward.Value)),
            BloodRewardPolicy.InitialPointsPercentage =>
                decimal.ToInt64(decimal.Round(points.InitialPoints * reward.Value / 100m)),
            BloodRewardPolicy.SolveTimePointsPercentage =>
                decimal.ToInt64(decimal.Round(solveTimePoints * reward.Value / 100m)),
            _ => throw new ArgumentOutOfRangeException(nameof(reward))
        };
}
