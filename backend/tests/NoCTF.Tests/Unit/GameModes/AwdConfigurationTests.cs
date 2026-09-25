using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdConfigurationTests
{
    [Test]
    public async Task Competition_configuration_exposes_hardening_and_round_defaults()
    {
        var configuration = new AwdConfiguration(
            HardeningDurationSeconds: 600,
            RoundDurationSeconds: 300,
            AttackRewardMode: AttackRewardMode.FixedPerAttack,
            AttackPoints: 50,
            VictimDefensePoolPoints: 100,
            CheckerIntervalSeconds: 30,
            ServiceHealthyPoints: 25,
            ServiceUnhealthyPenalty: 40);

        await Assert.That(AwdConfigurationValidator.Validate(configuration)).IsEmpty();
    }

    [Test]
    public async Task Challenge_configuration_uses_nullable_scoring_and_checker_overrides()
    {
        var parsed = new AwdChallengeConfiguration(
            AttackRewardMode: AttackRewardMode.SplitVictimDefensePool,
            AttackPoints: 0,
            VictimDefensePoolPoints: 0,
            CheckerIntervalSeconds: 15,
            ServiceHealthyPoints: 0,
            ServiceUnhealthyPenalty: 0);

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
    public async Task Invalid_flag_template_is_rejected_when_configuration_is_saved()
    {
        var configuration = AwdConfiguration.Default with
        {
            FlagTemplate = new PerTeamFlagTemplate("flag", "[UNKNOWN]", false)
        };

        var errors = AwdConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains("FlagTemplate is invalid.");
    }

    [Test]
    public async Task Challenge_runtime_requires_Awd_rotation_flags()
    {
        var configuration = new AwdChallengeConfiguration(
            Runtime: Runtime(
                RuntimeFlagSource.Static,
                RuntimeExposure.Participants),
            FlagInjection: new("printf '%s' '${FLAG}' > /dev/shm/flag"));

        var errors = AwdConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains("AWD runtimes must use AwdRotation flags.");
    }

    [Test]
    public async Task Challenge_runtime_requires_a_participant_visible_attack_entry()
    {
        var configuration = new AwdChallengeConfiguration(
            Runtime: Runtime(
                RuntimeFlagSource.AwdRotation,
                RuntimeExposure.OwnerOnly),
            FlagInjection: new("printf '%s' '${FLAG}' > /dev/shm/flag"));

        var errors = AwdConfigurationValidator.Validate(configuration);

        await Assert.That(errors)
            .Contains("AWD runtimes require at least one Participants access URL.");
    }

    private static ChallengeRuntimeTemplate Runtime(
        RuntimeFlagSource flagSource,
        RuntimeExposure exposure) =>
        new(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/awd:v1",
                Security: ContainerSecurityPolicy.Default,
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings:
            [
                new("nc {HOST} {PORT}", exposure, ContainerPort: 8080)
            ],
            FlagSource: flagSource);
}
