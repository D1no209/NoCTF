using NoCTF.GameModes.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ScoreCurveEvaluatorTests
{
    [Test]
    [Arguments(ScoreDecayMode.Fixed, 1000L, 1000L, 1000L)]
    [Arguments(ScoreDecayMode.Linear, 1000L, 600L, 100L)]
    [Arguments(ScoreDecayMode.Quadratic, 1000L, 822L, 100L)]
    [Arguments(ScoreDecayMode.Exponential, 1000L, 238L, 100L)]
    [Arguments(ScoreDecayMode.Logarithmic, 1000L, 371L, 100L)]
    public async Task Presets_have_stable_integer_endpoints_and_midpoints(
        ScoreDecayMode mode,
        long first,
        long fifth,
        long tenth)
    {
        var evaluator = new ScoreCurveEvaluator();
        var curve = new ScoreCurveConfiguration(1000, 100, 10, mode);

        await Assert.That(evaluator.Evaluate(curve, 1, 20)).IsEqualTo(first);
        await Assert.That(evaluator.Evaluate(curve, 5, 20)).IsEqualTo(fifth);
        await Assert.That(evaluator.Evaluate(curve, 10, 20)).IsEqualTo(tenth);
        await Assert.That(evaluator.Evaluate(curve, 100, 20)).IsEqualTo(tenth);
    }

    [Test]
    public async Task Custom_expression_uses_shared_variables_and_rounds_to_integer()
    {
        var evaluator = new ScoreCurveEvaluator();
        var curve = new ScoreCurveConfiguration(
            1000,
            100,
            10,
            ScoreDecayMode.Custom,
            "initialPoints - solveCount * 12.5m + eligibleTeamCount - decayTeamCount");

        var score = evaluator.Evaluate(curve, 5, 20);

        await Assert.That(score).IsEqualTo(948L);
        await Assert.That(evaluator.Validate(curve, 20)).IsEmpty();
    }

    [Test]
    public async Task Custom_expression_is_clamped_and_assignment_is_rejected()
    {
        var evaluator = new ScoreCurveEvaluator();
        var clamped = new ScoreCurveConfiguration(
            1000,
            100,
            10,
            ScoreDecayMode.Custom,
            "initialPoints * 100m");
        var assignment = clamped with { CustomExpression = "initialPoints = minimumPoints" };

        await Assert.That(evaluator.Evaluate(clamped, 1, 20)).IsEqualTo(1000L);
        await Assert.That(evaluator.Validate(assignment, 20)).IsNotEmpty();
    }

    [Test]
    public async Task Validation_checks_every_possible_team_count()
    {
        var evaluator = new ScoreCurveEvaluator();
        var curve = new ScoreCurveConfiguration(
            1000,
            100,
            10,
            ScoreDecayMode.Custom,
            "1m / (solveCount - 2)");

        await Assert.That(evaluator.Validate(curve, 5)).IsNotEmpty();
    }
}
