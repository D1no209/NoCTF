using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdScoreCalculatorTests
{
    [Test]
    public async Task RankMultiplierAndReadableRatioPreserveRepeatingFractions()
    {
        var rules = new AwdEffectiveRules(AwdAttackRewardMode.TargetRank, 3, 100, 100, 30, new());
        var value = AwdScoreCalculator.Attack(rules, AwdScoreCalculator.BudgetShare(1, 1, 3), 1, 4, 3);
        await Assert.That(AwdScoreCalculator.Aggregate([value, value, value])).IsEqualTo(4);
        var ratio = AwdScoreCalculator.SlaRatio(new() { SlaBasis = AwdSlaBasis.ReadableFlags }, AwdHealth.Recovering, 3, 1)!.Value;
        await Assert.That(AwdScoreCalculator.Aggregate([ratio, ratio, ratio])).IsEqualTo(1);
        await Assert.That(AwdScoreCalculator.Attack(rules, 1, 1, 4, null).Truncate()).IsEqualTo(3);
    }

    private static AwdEffectiveRules Rules(AwdAttackRewardMode mode = AwdAttackRewardMode.FixedPerAttack,
        AwdRuleOptions? options = null) => new(mode, 50, 100, 100, 30, options ?? new());

    [Test]
    [Arguments(1, 3)]
    [Arguments(2, 3)]
    [Arguments(3, 7)]
    [Arguments(1, 9)]
    [Arguments(1, 11)]
    [Arguments(1, 13)]
    [Arguments(7, 64)]
    public async Task MultipleFlagsDoNotMultiplyBudgetOrLoseIntegerRemainders(int stores, int flags)
    {
        var share = AwdScoreCalculator.BudgetShare(1, stores, flags);
        var rewards = Enumerable.Range(0, stores * flags).Select(_ => AwdScoreCalculator.Attack(Rules(), share, 1, 4, null));
        await Assert.That(AwdScoreCalculator.Aggregate(rewards)).IsEqualTo(50);
    }

    [Test]
    public async Task UnequalWeightsAndOrderingPreserveExactPositiveAndNegativeBuckets()
    {
        var rules = Rules();
        var shares = Enumerable.Repeat(AwdScoreCalculator.BudgetShare(2, 7, 3), 3)
            .Concat(Enumerable.Repeat(AwdScoreCalculator.BudgetShare(5, 7, 11), 11)).ToArray();
        var rewards = shares.Select(share => AwdScoreCalculator.Attack(rules, share, 1, 4, null)).ToArray();
        await Assert.That(AwdScoreCalculator.Aggregate(rewards)).IsEqualTo(50);
        await Assert.That(AwdScoreCalculator.Aggregate(rewards.Reverse())).IsEqualTo(50);
        await Assert.That(AwdScoreCalculator.Aggregate(shares.Select(share =>
            (AwdScoreFraction)0 - AwdScoreCalculator.DefenseLoss(rules, share, 1)))).IsEqualTo(-100);
        await Assert.That(AwdScoreCalculator.Aggregate(rewards.Take(1))).IsEqualTo(4);
        await Assert.That(default(AwdScoreFraction) + (AwdScoreFraction)0m).IsEqualTo((AwdScoreFraction)0m);
    }

    [Test]
    public async Task SharedPoolIsRedistributedWithoutMultiplyingVictimLoss()
    {
        var rules = Rules(AwdAttackRewardMode.SplitVictimDefensePool, new() { DefenseMode = AwdDefenseMode.SharedPool });
        await Assert.That(AwdScoreCalculator.Attack(rules, 1, 4, 5, null).ToDecimal()).IsEqualTo(25m);
        await Assert.That(AwdScoreCalculator.DefenseLoss(rules, 1, 4).ToDecimal()).IsEqualTo(100m);
    }

    [Test]
    public async Task ScarcityIsBoundedAndMonotone()
    {
        var rules = Rules(AwdAttackRewardMode.Scarcity);
        AwdScoreFraction previous = 50m;
        for (var captures = 1; captures <= 19; captures++)
        {
            var value = AwdScoreCalculator.Attack(rules, 1, captures, 20, null);
            await Assert.That(value <= previous && value >= 5).IsTrue();
            previous = value;
        }
        await Assert.That(previous.ToDecimal()).IsEqualTo(5m);
    }

    [Test]
    public async Task UnknownDoesNotBecomeSettledZeroAndMultiplierDoesNotEraseLoss()
    {
        await Assert.That(AwdScoreCalculator.SlaRatio(new(), AwdHealth.InternalError, 2, 1)).IsNull();
        await Assert.That(AwdScoreCalculator.ExtraPenalty(Rules(), null)).IsNull();
        await Assert.That(AwdScoreCalculator.Compose(new() { Composition = AwdScoreComposition.PositiveSlaMultiplier },
            50, 0, 100, 100, 10, 0)).IsEqualTo(-110m);
    }

    [Test]
    public async Task PenaltyUsesSlaBudgetAndCapsConsecutiveFailures()
    {
        var rules = Rules(options: new()
        {
            PenaltyMode = AwdPenaltyMode.Combined, OfflinePenalty = 20,
            OfflinePenaltyRatio = .3m, ConsecutiveFailureMultiplier = 2, MaximumRoundPenalty = 80
        });
        await Assert.That(AwdScoreCalculator.ExtraPenalty(rules, AwdHealth.Offline)).IsEqualTo(50m);
        await Assert.That(AwdScoreCalculator.ExtraPenalty(rules, AwdHealth.Offline, 100)).IsEqualTo(80m);
    }
}
