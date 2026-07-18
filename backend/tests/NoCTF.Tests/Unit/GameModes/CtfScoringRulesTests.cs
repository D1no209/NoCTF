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

    [Test]
    public async Task Configuration_parser_accepts_strict_camel_case_schema()
    {
        const string json = """
            {"schemaVersion":1,"flag":"flag{ok}","points":null,"bloodRewards":null}
            """;

        var result = CtfConfigurationUpgrader.ParseChallenge(json);

        await Assert.That(result.Flag).IsEqualTo("flag{ok}");
    }

    [Test]
    public async Task Configuration_parser_rejects_unknown_fields()
    {
        const string json = """
            {"schemaVersion":1,"flag":"flag{ok}","points":null,"bloodRewards":null,"unknown":true}
            """;

        void Act() => CtfConfigurationUpgrader.ParseChallenge(json);

        await Assert.That(Act).Throws<NoCTF.GameModes.Registration.GameModeConfigurationException>();
    }
}
