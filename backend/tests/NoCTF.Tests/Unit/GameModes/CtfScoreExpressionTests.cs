using NoCTF.GameModes.Ctf.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfScoreExpressionTests
{
    private const string DefaultExpression = """
        solveCount <= 1
            ? initialPoints
            : solveCount >= decayParameter
                ? minimumPoints
                : initialPoints
                  + (minimumPoints - initialPoints)
                  * ((solveCount - 1m) / (decayParameter - 1m))
                  * ((solveCount - 1m) / (decayParameter - 1m))
        """;

    [Test]
    public async Task Default_expression_uses_decimal_and_away_from_zero_rounding()
    {
        var evaluator = new CtfScoreExpression();

        var score = evaluator.Evaluate(DefaultExpression, new(1000m, 100m, 5, 20, 10m));

        await Assert.That(score).IsEqualTo(822L);
    }

    [Test]
    public async Task Assignment_is_rejected()
    {
        var evaluator = new CtfScoreExpression();

        var action = () => evaluator.Evaluate(
            "initialPoints = minimumPoints",
            new(1000m, 100m, 1, 20, 10m));

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task Result_is_clamped_to_configured_bounds()
    {
        var evaluator = new CtfScoreExpression();

        var score = evaluator.Evaluate("initialPoints * 100m", new(1000m, 100m, 1, 20, 10m));

        await Assert.That(score).IsEqualTo(1000L);
    }
}
