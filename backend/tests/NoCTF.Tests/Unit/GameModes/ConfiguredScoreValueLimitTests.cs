using NoCTF.Application.Scoring;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

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
            new((int)Maximum, (int)Maximum, 10),
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
                     ctfBase with { DefaultPoints = new((int)OverMaximum, 100, 10) },
                     ctfBase with
                     {
                         DefaultPoints = new((int)OverMaximum, (int)OverMaximum, 10)
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
                         Break = awdpBase.Break with { Points = OverMaximum }
                     },
                     awdpBase with { Fix = awdpBase.Fix with { Points = OverMaximum } },
                     awdpBase with { BreakWrongPenalty = OverMaximum },
                     awdpBase with { FixFailurePenalty = OverMaximum },
                     awdpBase with { ViolationPenalty = OverMaximum },
                     awdpBase with { ServiceDownPenalty = OverMaximum }
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
            Points: null,
            BloodRewards: null);
        foreach (var configuration in new[]
                 {
                     ctfBase with { Points = new((int)OverMaximum, 100, 10) },
                     ctfBase with
                     {
                         Points = new((int)OverMaximum, (int)OverMaximum, 10)
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
                         Break = awdpBase.Break! with { Points = OverMaximum }
                     },
                     awdpBase with { Fix = awdpBase.Fix! with { Points = OverMaximum } },
                     awdpBase with { BreakWrongPenalty = OverMaximum },
                     awdpBase with { FixFailurePenalty = OverMaximum },
                     awdpBase with { ViolationPenalty = OverMaximum },
                     awdpBase with { ServiceDownPenalty = OverMaximum }
                 })
        {
            await Assert.That(AwdpConfigurationValidator.Validate(configuration)).IsNotEmpty();
        }

        await Assert.That(KohConfigurationValidator.Validate(new KohChallengeConfiguration(
                KohChallengeConfiguration.CurrentSchemaVersion,
                ControlPointsPerInterval: OverMaximum)))
            .IsNotEmpty();
    }

    private static AwdpConfiguration CreateAwdpCompetition(long value) => new(
        AwdpConfiguration.CurrentSchemaVersion,
        RoundDurationSeconds: 300,
        Break: new(AchievementSettlement.PerRound, value),
        Fix: new(AchievementSettlement.PerRound, value),
        ViolationPenalty: value,
        ServiceDownPenalty: value,
        BreakWrongPenalty: value,
        FixFailurePenalty: value);

    private static AwdpChallengeConfiguration CreateAwdpChallenge(long value) => new(
        AwdpChallengeConfiguration.CurrentSchemaVersion,
        Break: new(AchievementSettlement.PerRound, value),
        Fix: new(AchievementSettlement.PerRound, value),
        RequireBreakBeforeFix: null,
        MaxBreakSubmissions: null,
        MaxFixSubmissions: null,
        BreakWrongPenalty: value,
        FixFailurePenalty: value,
        ViolationPenalty: value,
        ServiceDownPenalty: value);
}
