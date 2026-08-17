using NoCTF.Application.Scoring;

namespace NoCTF.GameModes.Scoring;

public enum ScoreDecayMode
{
    Fixed,
    Linear,
    Quadratic,
    Exponential,
    Logarithmic,
    Custom
}

public sealed record ScoreCurveConfiguration(
    long InitialPoints,
    long MinimumPoints,
    int DecayTeamCount,
    ScoreDecayMode DecayMode = ScoreDecayMode.Quadratic,
    string? CustomExpression = null)
{
    public static ScoreCurveConfiguration Default { get; } = new(500, 100, 10);
}

public sealed class ScoreCurveEvaluator
{
    private readonly ScoreCurveExpression expression = new();

    public long Evaluate(
        ScoreCurveConfiguration configuration,
        int solveCount,
        int eligibleTeamCount)
    {
        var normalizedSolveCount = Math.Max(1, solveCount);
        var raw = configuration.DecayMode switch
        {
            ScoreDecayMode.Fixed => configuration.InitialPoints,
            ScoreDecayMode.Linear => EvaluateLinear(configuration, normalizedSolveCount),
            ScoreDecayMode.Quadratic => EvaluateQuadratic(configuration, normalizedSolveCount),
            ScoreDecayMode.Exponential => EvaluateExponential(configuration, normalizedSolveCount),
            ScoreDecayMode.Logarithmic => EvaluateLogarithmic(configuration, normalizedSolveCount),
            ScoreDecayMode.Custom => expression.Evaluate(
                configuration.CustomExpression ?? string.Empty,
                new(
                    configuration.InitialPoints,
                    configuration.MinimumPoints,
                    normalizedSolveCount,
                    eligibleTeamCount,
                    configuration.DecayTeamCount)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(configuration),
                configuration.DecayMode,
                "Unsupported score decay mode.")
        };
        return BoundAndRound(raw, configuration);
    }

    public IReadOnlyList<string> Validate(
        ScoreCurveConfiguration configuration,
        int eligibleTeamCount)
    {
        var errors = new List<string>();
        if (configuration.InitialPoints <= 0)
            errors.Add("InitialPoints must be positive.");
        if (configuration.InitialPoints > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"InitialPoints cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.MinimumPoints is < 0 || configuration.MinimumPoints > configuration.InitialPoints)
            errors.Add("MinimumPoints must be between zero and InitialPoints.");
        if (configuration.MinimumPoints > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"MinimumPoints cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.DecayTeamCount <= 1)
            errors.Add("DecayTeamCount must be greater than one.");
        if (!Enum.IsDefined(configuration.DecayMode))
            errors.Add("DecayMode is invalid.");
        if (configuration.DecayMode == ScoreDecayMode.Custom)
        {
            if (string.IsNullOrWhiteSpace(configuration.CustomExpression))
            {
                errors.Add("CustomExpression is required for Custom decay mode.");
            }
            else
            {
                try
                {
                    expression.Validate(
                        configuration.CustomExpression,
                        configuration.InitialPoints,
                        configuration.MinimumPoints,
                        configuration.DecayTeamCount,
                        eligibleTeamCount);
                }
                catch (Exception exception) when (ScoreCurveExpression.IsValidationException(exception))
                {
                    errors.Add($"CustomExpression is invalid: {exception.Message}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(configuration.CustomExpression))
        {
            errors.Add("CustomExpression is only valid for Custom decay mode.");
        }
        return errors;
    }

    private static decimal EvaluateLinear(ScoreCurveConfiguration configuration, int solveCount)
    {
        var progress = Progress(configuration, solveCount);
        return configuration.InitialPoints
            + (configuration.MinimumPoints - configuration.InitialPoints) * progress;
    }

    private static decimal EvaluateQuadratic(ScoreCurveConfiguration configuration, int solveCount)
    {
        var progress = Progress(configuration, solveCount);
        return configuration.InitialPoints
            + (configuration.MinimumPoints - configuration.InitialPoints) * progress * progress;
    }

    private static decimal EvaluateExponential(ScoreCurveConfiguration configuration, int solveCount)
    {
        var progress = (double)Progress(configuration, solveCount);
        const double steepness = 4d;
        var normalized = (Math.Exp(-steepness * progress) - Math.Exp(-steepness))
            / (1d - Math.Exp(-steepness));
        return configuration.MinimumPoints
            + (configuration.InitialPoints - configuration.MinimumPoints) * (decimal)normalized;
    }

    private static decimal EvaluateLogarithmic(ScoreCurveConfiguration configuration, int solveCount)
    {
        var progress = (double)Progress(configuration, solveCount);
        var normalized = Math.Log10(1d + 9d * progress);
        return configuration.InitialPoints
            + (configuration.MinimumPoints - configuration.InitialPoints) * (decimal)normalized;
    }

    private static decimal Progress(ScoreCurveConfiguration configuration, int solveCount) =>
        decimal.Clamp(
            (solveCount - 1m) / (configuration.DecayTeamCount - 1m),
            0m,
            1m);

    private static long BoundAndRound(decimal raw, ScoreCurveConfiguration configuration)
    {
        var bounded = decimal.Clamp(raw, configuration.MinimumPoints, configuration.InitialPoints);
        return checked((long)decimal.Round(bounded, 0, MidpointRounding.AwayFromZero));
    }
}
