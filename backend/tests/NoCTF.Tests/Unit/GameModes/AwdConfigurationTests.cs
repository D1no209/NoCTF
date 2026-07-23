using System.Text.Json;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdConfigurationTests
{
    [Test]
    public async Task Competition_configuration_exposes_hardening_and_round_defaults()
    {
        var configuration = new AwdConfiguration(
            AwdConfiguration.CurrentSchemaVersion,
            HardeningDurationSeconds: 600,
            RoundDurationSeconds: 300,
            AttackRewardMode: AttackRewardMode.FixedPerAttack,
            AttackPoints: 50,
            VictimDefensePoolPoints: 100,
            CheckerIntervalSeconds: 30,
            ServiceHealthyPoints: 25,
            ServiceUnhealthyPenalty: 40);

        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var parsed = AwdConfigurationUpgrader.ParseCompetition(json);

        await Assert.That(parsed).IsEqualTo(configuration);
        await Assert.That(AwdConfigurationValidator.Validate(parsed)).IsEmpty();
    }

    [Test]
    public async Task Challenge_configuration_uses_nullable_scoring_and_checker_overrides()
    {
        const string json = """
            {
              "schemaVersion": 3,
              "attackRewardMode": "SplitVictimDefensePool",
              "attackPoints": 0,
              "victimDefensePoolPoints": 0,
              "checkerIntervalSeconds": 15,
              "serviceHealthyPoints": 0,
              "serviceUnhealthyPenalty": 0,
              "runtime": null,
              "checker": null,
              "flagInjection": null
            }
            """;

        var parsed = AwdConfigurationUpgrader.ParseChallenge(json);

        await Assert.That(parsed.AttackRewardMode).IsEqualTo(AttackRewardMode.SplitVictimDefensePool);
        await Assert.That(parsed.AttackPoints).IsEqualTo(0L);
        await Assert.That(parsed.VictimDefensePoolPoints).IsEqualTo(0L);
        await Assert.That(parsed.CheckerIntervalSeconds).IsEqualTo(15);
        await Assert.That(AwdConfigurationValidator.Validate(parsed)).IsEmpty();
    }

    [Test]
    public async Task Invalid_competition_ranges_are_rejected()
    {
        var configuration = new AwdConfiguration(
            AwdConfiguration.CurrentSchemaVersion,
            HardeningDurationSeconds: -1,
            RoundDurationSeconds: 0,
            AttackRewardMode: AttackRewardMode.FixedPerAttack,
            AttackPoints: -1,
            VictimDefensePoolPoints: -1,
            CheckerIntervalSeconds: 0,
            ServiceHealthyPoints: -1,
            ServiceUnhealthyPenalty: -1);

        var errors = AwdConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains("HardeningDurationSeconds cannot be negative.");
        await Assert.That(errors).Contains("RoundDurationSeconds must be positive.");
        await Assert.That(errors).Contains("CheckerIntervalSeconds must be positive.");
        await Assert.That(errors).Contains("Scoring values cannot be negative.");
    }

    [Test]
    public async Task Numeric_attack_reward_mode_is_rejected_at_the_json_boundary()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "hardeningDurationSeconds": 0,
              "roundDurationSeconds": 300,
              "attackRewardMode": 99,
              "attackPoints": 50,
              "victimDefensePoolPoints": 100,
              "checkerIntervalSeconds": 30,
              "serviceHealthyPoints": 100,
              "serviceUnhealthyPenalty": 50
            }
            """;

        await Assert.That(() => AwdConfigurationUpgrader.ParseCompetition(json))
            .Throws<GameModeConfigurationException>();
    }
}
