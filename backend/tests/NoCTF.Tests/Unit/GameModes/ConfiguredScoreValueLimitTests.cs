using NoCTF.Application.Scoring;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ConfiguredScoreValueLimitTests
{
    private const long Maximum = ScoreValueLimits.MaximumConfiguredValue;
    private const long OverMaximum = Maximum + 1;

    [Test]
    public async Task Competition_score_values_accept_the_configured_maximum()
    {
        var ctf = new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(Maximum, Maximum, 10),
            [new(BloodRewardPolicy.FixedPoints, Maximum)],
            WrongSubmissionPenalty: Maximum);
        var awd = AwdConfiguration.Default with
        {
            AttackPoints = Maximum,
            VictimDefensePoolPoints = Maximum,
            ServiceHealthyPoints = Maximum,
            ServiceUnhealthyPenalty = Maximum
        };
        var awdp = CreateAwdpCompetition(Maximum);
        var koh = new KohConfiguration(
            KohConfiguration.CurrentSchemaVersion,
            PollIntervalSeconds: 5,
            ControlPointsPerInterval: Maximum);

        await Assert.That(CtfConfigurationValidator.Validate(ctf)).IsEmpty();
        await Assert.That(AwdConfigurationValidator.Validate(awd)).IsEmpty();
        await Assert.That(AwdpConfigurationValidator.Validate(awdp)).IsEmpty();
        await Assert.That(KohConfigurationValidator.Validate(koh)).IsEmpty();
    }

    [Test]
    public async Task Competition_score_values_reject_each_value_above_the_maximum()
    {
        var ctfBase = new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(500, 100, 10),
            []);
        foreach (var configuration in new[]
                 {
                     ctfBase with { DefaultScoreCurve = new(OverMaximum, 100, 10) },
                     ctfBase with
                     {
                          DefaultScoreCurve = new(OverMaximum, OverMaximum, 10)
                     },
                     ctfBase with
                     {
                         BloodRewards =
                         [new(BloodRewardPolicy.FixedPoints, OverMaximum)]
                     },
                     ctfBase with { WrongSubmissionPenalty = OverMaximum }
                 })
        {
            await Assert.That(CtfConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        var awdBase = AwdConfiguration.Default;
        foreach (var configuration in new[]
                 {
                     awdBase with { AttackPoints = OverMaximum },
                     awdBase with { VictimDefensePoolPoints = OverMaximum },
                     awdBase with { ServiceHealthyPoints = OverMaximum },
                     awdBase with { ServiceUnhealthyPenalty = OverMaximum }
                 })
        {
            await Assert.That(AwdConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        var awdpBase = CreateAwdpCompetition(100);
        foreach (var configuration in new[]
                 {
                     awdpBase with
                     {
                          Break = awdpBase.Break with { InitialPoints = OverMaximum }
                     },
                      awdpBase with { Fix = awdpBase.Fix with { InitialPoints = OverMaximum } },
                     awdpBase with { FlagWrongPenalty = OverMaximum },
                     awdpBase with { ExploitSucceededPenalty = OverMaximum },
                     awdpBase with { ServiceAbnormalPenalty = OverMaximum }
                 })
        {
            await Assert.That(AwdpConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        await Assert.That(KohConfigurationValidator.Validate(new KohConfiguration(
                KohConfiguration.CurrentSchemaVersion,
                PollIntervalSeconds: 5,
                ControlPointsPerInterval: OverMaximum)))
            .IsNotEmpty();
    }

    [Test]
    public async Task Challenge_score_overrides_accept_the_configured_maximum()
    {
        var ctf = new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            new((int)Maximum, (int)Maximum, 10),
            [new(BloodRewardPolicy.FixedPoints, Maximum)],
            WrongSubmissionPenalty: Maximum);
        var awd = new AwdChallengeConfiguration(
            AwdChallengeConfiguration.CurrentSchemaVersion,
            AttackPoints: Maximum,
            VictimDefensePoolPoints: Maximum,
            ServiceHealthyPoints: Maximum,
            ServiceUnhealthyPenalty: Maximum);
        var awdp = CreateAwdpChallenge(Maximum);
        var koh = new KohChallengeConfiguration(
            KohChallengeConfiguration.CurrentSchemaVersion,
            ControlPointsPerInterval: Maximum);

        await Assert.That(CtfConfigurationValidator.Validate(ctf)).IsEmpty();
        await Assert.That(AwdConfigurationValidator.Validate(awd)).IsEmpty();
        await Assert.That(AwdpConfigurationValidator.Validate(awdp)).IsEmpty();
        await Assert.That(KohConfigurationValidator.Validate(koh)).IsEmpty();
    }

    [Test]
    public async Task Challenge_score_overrides_reject_each_value_above_the_maximum()
    {
        var ctfBase = new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            ScoreCurve: null,
            BloodRewards: null);
        foreach (var configuration in new[]
                 {
                      ctfBase with { ScoreCurve = new(OverMaximum, 100, 10) },
                     ctfBase with
                     {
                          ScoreCurve = new(OverMaximum, OverMaximum, 10)
                     },
                     ctfBase with
                     {
                         BloodRewards =
                         [new(BloodRewardPolicy.FixedPoints, OverMaximum)]
                     },
                     ctfBase with { WrongSubmissionPenalty = OverMaximum }
                 })
        {
            await Assert.That(CtfConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        var awdBase = new AwdChallengeConfiguration(
            AwdChallengeConfiguration.CurrentSchemaVersion);
        foreach (var configuration in new[]
                 {
                     awdBase with { AttackPoints = OverMaximum },
                     awdBase with { VictimDefensePoolPoints = OverMaximum },
                     awdBase with { ServiceHealthyPoints = OverMaximum },
                     awdBase with { ServiceUnhealthyPenalty = OverMaximum }
                 })
        {
            await Assert.That(AwdConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        var awdpBase = CreateAwdpChallenge(100);
        foreach (var configuration in new[]
                 {
                     awdpBase with
                     {
                          Break = awdpBase.Break! with { InitialPoints = OverMaximum }
                     },
                      awdpBase with { Fix = awdpBase.Fix! with { InitialPoints = OverMaximum } },
                     awdpBase with { FlagWrongPenalty = OverMaximum },
                     awdpBase with { ExploitSucceededPenalty = OverMaximum },
                     awdpBase with { ServiceAbnormalPenalty = OverMaximum }
                 })
        {
            await Assert.That(AwdpConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        await Assert.That(KohConfigurationValidator.Validate(new KohChallengeConfiguration(
                KohChallengeConfiguration.CurrentSchemaVersion,
                ControlPointsPerInterval: OverMaximum)))
            .IsNotEmpty();
    }

    [Test]
    public async Task Score_limit_does_not_apply_to_upload_bytes_durations_or_percentages()
    {
        var uploadConfiguration = CreateAwdpChallenge(100) with
        {
            MaximumPatchUploadBytes = OverMaximum
        };
        var durationConfiguration = AwdConfiguration.Default with
        {
            HardeningDurationSeconds = (int)OverMaximum,
            RoundDurationSeconds = (int)OverMaximum
        };
        var percentageConfiguration = new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(500, 100, 10),
            [new(BloodRewardPolicy.CurrentPointsPercentage, 100)]);

        await Assert.That(AwdpConfigurationValidator.Validate(uploadConfiguration)).IsEmpty();
        await Assert.That(AwdConfigurationValidator.Validate(durationConfiguration)).IsEmpty();
        await Assert.That(CtfConfigurationValidator.Validate(percentageConfiguration)).IsEmpty();
    }

    private static AwdpConfiguration CreateAwdpCompetition(long value) => new(
        AwdpConfiguration.CurrentSchemaVersion,
        RoundDurationSeconds: 300,
        Break: new(value, value, 2, ScoreDecayMode.Fixed),
        Fix: new(value, value, 2, ScoreDecayMode.Fixed),
        FlagWrongPenalty: value,
        ExploitSucceededPenalty: value,
        ServiceAbnormalPenalty: value,
        RequireBreakBeforeFix: false,
        MaxBreakSubmissions: 10,
        MaxFixSubmissions: 10);

    private static AwdpChallengeConfiguration CreateAwdpChallenge(long value) => new(
        AwdpChallengeConfiguration.CurrentSchemaVersion,
        Break: new(value, value, 2, ScoreDecayMode.Fixed),
        Fix: new(value, value, 2, ScoreDecayMode.Fixed),
        RequireBreakBeforeFix: null,
        MaxBreakSubmissions: null,
        MaxFixSubmissions: null,
        FlagWrongPenalty: value,
        ExploitSucceededPenalty: value,
        ServiceAbnormalPenalty: value);
}
