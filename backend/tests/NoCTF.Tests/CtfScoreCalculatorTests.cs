using NoCTF.Application.Scoring;
using NoCTF.Core;

namespace NoCTF.Tests;

public class CtfScoreCalculatorTests
{
    [Fact]
    public void CalculateChallengePoints_SecondSolveShowsSmallDecayWithDefaultCtfScale()
    {
        var config = new PointsConfig(
            InitialPoints: 500,
            MinimumPoints: 100,
            DecayFactor: 450,
            DecayFunction: CtfScoreCalculator.FunctionQuadratic);

        var score = CtfScoreCalculator.CalculateChallengePoints(2, config);

        Assert.Equal(499, score);
    }

    [Fact]
    public void CalculateChallengePoints_HigherDifficultySlowsDecay()
    {
        var config = new PointsConfig(
            InitialPoints: 500,
            MinimumPoints: 100,
            DecayFactor: 10,
            DecayFunction: CtfScoreCalculator.FunctionLinear);

        var normalDifficulty = CtfScoreCalculator.CalculateChallengePoints(5, config, difficultyCoefficient: 1);
        var highDifficulty = CtfScoreCalculator.CalculateChallengePoints(5, config, difficultyCoefficient: 2);

        Assert.True(highDifficulty > normalDifficulty);
    }

    [Fact]
    public void CalculateBloodBonus_UsesCurrentChallengePoints()
    {
        var competition = new Competition
        {
            FirstBloodBonusPercent = 20,
            SecondBloodBonusPercent = 10,
            ThirdBloodBonusPercent = 5
        };

        var bonus = CtfScoreCalculator.CalculateBloodBonus(rank: 1, currentPoints: 498, competition, enabled: true);

        Assert.Equal(99, bonus);
    }
}
