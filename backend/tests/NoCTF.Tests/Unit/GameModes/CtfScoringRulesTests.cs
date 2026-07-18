using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Ctf.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public class CtfScoringRulesTests
{
    [Test]
    public async Task Points_decay_never_falls_below_minimum()
    {
        var points = new CtfPointConfiguration(500, 100, 450);

        var result = CtfScoringRules.CalculatePoints(points, 1_000_000);

        await Assert.That(result).IsGreaterThanOrEqualTo(100);
    }

    [Test]
    [Arguments(BloodRewardPolicy.FixedPoints, 25, 25)]
    [Arguments(BloodRewardPolicy.InitialPointsPercentage, 10, 50)]
    [Arguments(BloodRewardPolicy.SolveTimePointsPercentage, 10, 20)]
    public async Task Blood_policy_is_explicit(
        BloodRewardPolicy policy,
        decimal value,
        long expected)
    {
        var result = CtfScoringRules.CalculateBlood(
            new BloodReward(policy, value),
            new CtfPointConfiguration(500, 100, 450),
            200);

        await Assert.That(result).IsEqualTo(expected);
    }
}
