using NoCTF.Core;

namespace NoCTF.Application.Scoring;

public static class CtfScoreCalculator
{
    public const string FunctionLinear = "linear";
    public const string FunctionQuadratic = "quadratic";
    public const string FunctionLogarithmic = "logarithmic";
    public const string FunctionSigmoid = "sigmoid";

    public static int CalculateChallengePoints(int solveCount, PointsConfig config, double difficultyCoefficient = 1.0)
    {
        if (solveCount <= 1)
            return config.InitialPoints;

        var initial = Math.Max(config.InitialPoints, config.MinimumPoints);
        var minimum = Math.Min(config.MinimumPoints, initial);
        var range = initial - minimum;
        if (range <= 0)
            return minimum;

        var effectiveDecay = Math.Max(1.0, config.DecayFactor * Math.Max(0.1, difficultyCoefficient));
        var progress = Math.Max(0.0, solveCount / effectiveDecay);
        var normalized = Normalize(progress, config.DecayFunction);
        var value = initial - range * normalized;
        return Math.Max(minimum, Math.Min(initial, (int)Math.Floor(value)));
    }

    public static int CalculateBloodBonus(int rank, int currentPoints, Competition competition, bool enabled)
    {
        if (!enabled || rank is < 1 or > 3 || currentPoints <= 0)
            return 0;

        var percent = rank switch
        {
            1 => competition.FirstBloodBonusPercent,
            2 => competition.SecondBloodBonusPercent,
            3 => competition.ThirdBloodBonusPercent,
            _ => 0
        };

        if (percent <= 0)
            return 0;

        return (int)Math.Floor(currentPoints * percent / 100.0);
    }

    private static double Normalize(double progress, string? function)
    {
        var key = string.IsNullOrWhiteSpace(function)
            ? FunctionSigmoid
            : function.Trim().ToLowerInvariant();

        return key switch
        {
            FunctionLinear => Math.Min(1.0, progress),
            FunctionQuadratic => Math.Min(1.0, progress * progress),
            FunctionLogarithmic => progress >= 1
                ? 1.0
                : Math.Log(1 + progress * 9) / Math.Log(10),
            _ => SigmoidSlowFastSlow(progress)
        };
    }

    private static double SigmoidSlowFastSlow(double progress)
    {
        if (progress >= 1)
            return 1.0;

        var x = Math.Max(0.0, progress);
        var start = Logistic(0);
        var end = Logistic(1);
        return Math.Clamp((Logistic(x) - start) / (end - start), 0.0, 1.0);

        static double Logistic(double value) => 1.0 / (1.0 + Math.Exp(-10.0 * (value - 0.5)));
    }
}
