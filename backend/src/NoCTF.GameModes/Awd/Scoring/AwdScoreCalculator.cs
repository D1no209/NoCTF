using NoCTF.Application.Scoring;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.GameModes.Awd.Scoring;

public static class AwdScoreCalculator
{
    public static AwdScoreFraction BudgetShare(decimal storeWeight, decimal totalStoreWeight, int flagsPerIssue)
    {
        if (storeWeight <= 0 || totalStoreWeight < storeWeight || flagsPerIssue <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeWeight));
        return (AwdScoreFraction)storeWeight / totalStoreWeight / flagsPerIssue;
    }

    public static AwdScoreFraction Attack(AwdEffectiveRules rules, AwdScoreFraction share, int captures, int eligibleTeams, int? victimRank)
    {
        if (captures <= 0 || eligibleTeams < 2) return 0;
        if (captures > eligibleTeams - 1 || share <= 0 || share > 1)
            throw new ArgumentOutOfRangeException(nameof(captures));
        var maximum = rules.AttackPoints * share;
        return rules.AttackMode switch
        {
            AwdAttackRewardMode.FixedPerAttack => maximum,
            AwdAttackRewardMode.SplitVictimDefensePool => rules.DefensePoints * share / captures,
            AwdAttackRewardMode.Scarcity => maximum == 0 ? 0 : maximum * ScarcityRatio(
                captures, eligibleTeams, rules.Options.MinimumAttackRatio, rules.Options.ScarcityAlpha),
            AwdAttackRewardMode.TargetRank => maximum * RankMultiplier(victimRank, eligibleTeams, rules.Options.MaximumRankMultiplier),
            _ => throw new ArgumentOutOfRangeException(nameof(rules.AttackMode))
        };
    }

    public static AwdScoreFraction DefenseLoss(AwdEffectiveRules rules, AwdScoreFraction share, int captures) => captures <= 0 ? 0 :
        rules.DefensePoints * share * (rules.Options.DefenseMode == AwdDefenseMode.PerCapture ? captures : 1);

    public static AwdScoreFraction? SlaRatio(AwdRuleOptions options, AwdHealth? health, int requiredFlags, int readableFlags)
    {
        if (health is null or AwdHealth.InternalError) return null;
        if (requiredFlags < 0 || readableFlags < 0 || readableFlags > requiredFlags)
            throw new ArgumentOutOfRangeException(nameof(readableFlags));
        if (options.SlaBasis == AwdSlaBasis.ReadableFlags && health is AwdHealth.Ok or AwdHealth.Recovering)
            return requiredFlags == 0 ? 1 : (AwdScoreFraction)readableFlags / requiredFlags;
        return health switch
        {
            AwdHealth.Ok => options.OkRatio,
            AwdHealth.Mumble => options.MumbleRatio,
            AwdHealth.Offline => options.OfflineRatio,
            AwdHealth.Recovering => options.RecoveringRatio,
            _ => throw new ArgumentOutOfRangeException(nameof(health))
        };
    }

    public static decimal? ExtraPenalty(AwdEffectiveRules rules, AwdHealth? health, int consecutiveFailures = 1)
    {
        if (health is null or AwdHealth.InternalError) return null;
        var options = rules.Options;
        var (amount, ratio) = health switch
        {
            AwdHealth.Mumble => (options.MumblePenalty, options.MumblePenaltyRatio),
            AwdHealth.Offline => (options.OfflinePenalty, options.OfflinePenaltyRatio),
            AwdHealth.Recovering => (options.RecoveringPenalty, options.RecoveringPenaltyRatio),
            _ => (0L, 0m)
        };
        var result = options.PenaltyMode switch
        {
            AwdPenaltyMode.SlaRewardOnly => 0,
            AwdPenaltyMode.Fixed => (decimal)amount,
            AwdPenaltyMode.Proportional => rules.SlaPoints * ratio,
            AwdPenaltyMode.Combined => amount + rules.SlaPoints * ratio,
            _ => throw new ArgumentOutOfRangeException(nameof(options.PenaltyMode))
        };
        var cap = options.MaximumRoundPenalty ?? ScoreValueLimits.MaximumConfiguredValue;
        result = Math.Min(result, cap);
        if (result > 0 && options.ConsecutiveFailureMultiplier > 1)
            for (var index = 1; index < consecutiveFailures && result < cap; index++)
                result = Math.Min(cap, result * options.ConsecutiveFailureMultiplier);
        return result;
    }

    public static AwdScoreFraction Compose(AwdRuleOptions options, AwdScoreFraction attack, AwdScoreFraction defenseReward, AwdScoreFraction defenseLoss,
        AwdScoreFraction sla, AwdScoreFraction extraPenalty, AwdScoreFraction availability)
    {
        return options.Composition switch
        {
            AwdScoreComposition.Additive => attack + defenseReward - defenseLoss + sla - extraPenalty,
            AwdScoreComposition.Weighted => attack * options.AttackWeight
                + (defenseReward - defenseLoss) * options.DefenseWeight + sla * options.SlaWeight - extraPenalty,
            AwdScoreComposition.PositiveSlaMultiplier => (attack + defenseReward) * availability - defenseLoss - extraPenalty,
            _ => throw new ArgumentOutOfRangeException(nameof(options.Composition))
        };
    }

    public static long Aggregate(IEnumerable<AwdScoreFraction> values) =>
        values.Aggregate((AwdScoreFraction)0m, (total, value) => total + value).Truncate();

    private static decimal ScarcityRatio(int captures, int teams, decimal minimum, decimal alpha)
    {
        if (captures == 1) return 1;
        if (captures == teams - 1) return minimum;
        var exponent = Math.Pow((double)(captures - 1) / Math.Max(1, teams - 2), (double)alpha);
        return decimal.Round((decimal)Math.Pow((double)minimum, exponent), 12, MidpointRounding.ToEven);
    }

    private static AwdScoreFraction RankMultiplier(int? rank, int teams, decimal maximum) =>
        rank is null ? 1 : rank <= 0 || rank > teams
            ? throw new ArgumentOutOfRangeException(nameof(rank))
            : 1 + (AwdScoreFraction)(maximum - 1) * (teams - rank.Value) / (teams - 1);
}
