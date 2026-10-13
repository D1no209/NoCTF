using NoCTF.Application.Scoring;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdEffectiveRules(
    AwdAttackRewardMode AttackMode,
    long AttackPoints,
    long DefensePoints,
    long SlaPoints,
    int CheckerIntervalSeconds,
    AwdRuleOptions Options)
{
    public static AwdEffectiveRules Resolve(AwdCompetitionModeConfiguration competition, AwdCompetitionChallengeRules challenge)
    {
        var options = challenge.AwdOverrides.Resolve(competition.AwdOptions);
        var attack = challenge.AttackRewardMode ?? competition.AttackRewardMode;
        (attack, options) = options.Preset switch
        {
            AwdScoringPreset.Fixed => (AwdAttackRewardMode.FixedPerAttack,
                options with { DefenseMode = AwdDefenseMode.FirstLeak, Composition = AwdScoreComposition.Additive }),
            AwdScoringPreset.SharedPool => (AwdAttackRewardMode.SplitVictimDefensePool,
                options with { DefenseMode = AwdDefenseMode.SharedPool, Composition = AwdScoreComposition.Additive,
                    AttackSettlement = AwdAttackSettlement.FlagExpiry }),
            AwdScoringPreset.Scarcity => (AwdAttackRewardMode.Scarcity,
                options with { DefenseMode = AwdDefenseMode.FirstLeak, Composition = AwdScoreComposition.Additive,
                    AttackSettlement = AwdAttackSettlement.FlagExpiry }),
            AwdScoringPreset.TargetRank => (AwdAttackRewardMode.TargetRank,
                options with { DefenseMode = AwdDefenseMode.FirstLeak, Composition = AwdScoreComposition.Additive }),
            AwdScoringPreset.SlaMultiplier => (AwdAttackRewardMode.FixedPerAttack,
                options with { DefenseMode = AwdDefenseMode.FirstLeak, Composition = AwdScoreComposition.PositiveSlaMultiplier }),
            AwdScoringPreset.Custom => (attack, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Preset))
        };
        return new(attack, challenge.AttackPoints ?? competition.AttackPoints,
            challenge.VictimDefensePoolPoints ?? competition.VictimDefensePoolPoints,
            challenge.ServiceHealthyPoints ?? competition.ServiceHealthyPoints,
            challenge.CheckerIntervalSeconds ?? competition.CheckerIntervalSeconds, options);
    }

    public static IReadOnlyList<string> ValidateConfiguration(
        AwdCompetitionModeConfiguration competition, AwdCompetitionChallengeRules challenge)
    {
        var options = challenge.AwdOverrides.Resolve(competition.AwdOptions);
        if (!Enum.IsDefined(options.Preset)) return ["Unknown AWD scoring preset."];
        var errors = Resolve(competition, challenge).Validate().ToList();
        if (options.SlaSettlement == AwdSlaSettlement.TimeWeighted)
            errors.Add("Time-weighted AWD settlement is unavailable; use FinalScheduledCheck or SampleAverage.");
        if (challenge.StoreRules.Select(item => item.StoreId).Distinct().Count() != challenge.StoreRules.Count
            || challenge.StoreRules.Any(item => item.StoreId == Guid.Empty || item.Weight is <= 0 or > 100
                || decimal.Round(item.Weight, 6) != item.Weight || item.FlagsPerIssue is < 1 or > 64))
            errors.Add("AWD store rules require unique store ids, positive weights up to 100 and 1..64 flags.");
        return errors;
    }

    public static AwdEffectiveRules FromSnapshot(AwdRoundRules rules) => new(
        rules.AttackRewardMode, rules.AttackPoints, rules.VictimDefensePoolPoints,
        rules.ServiceHealthyPoints, rules.CheckerIntervalSeconds, rules.Options);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var value = Options;
        if (new[] { value.OkRatio, value.RecoveringRatio, value.MumbleRatio, value.OfflineRatio,
                value.MumblePenaltyRatio, value.OfflinePenaltyRatio, value.RecoveringPenaltyRatio,
                value.ConsecutiveFailureMultiplier, value.MinimumAttackRatio, value.ScarcityAlpha,
                value.MaximumRankMultiplier, value.AttackWeight, value.DefenseWeight, value.SlaWeight }
            .Any(number => decimal.Round(number, 6) != number))
            errors.Add("AWD decimal configuration values support at most six fractional digits.");
        if (!Enum.IsDefined(AttackMode) || !Enum.IsDefined(value.Preset)
            || !Enum.IsDefined(value.FlagLifetimeUnit) || !Enum.IsDefined(value.HistoricalLifetimeUnit)
            || !Enum.IsDefined(value.GenerationPolicy) || !Enum.IsDefined(value.DefenseMode)
            || !Enum.IsDefined(value.SlaBasis) || !Enum.IsDefined(value.SlaSettlement)
            || !Enum.IsDefined(value.PenaltyMode) || !Enum.IsDefined(value.Composition)
            || !Enum.IsDefined(value.AttackSettlement))
            errors.Add("AWD policy contains an unknown enum value.");
        if (value.FlagLifetime <= 0 || value.HistoricalLifetime <= 0
            || value.FlagLifetime > LifetimeMaximum(value.FlagLifetimeUnit)
            || value.HistoricalLifetime > LifetimeMaximum(value.HistoricalLifetimeUnit))
            errors.Add("AWD lifetime must be 1..1000 rounds or 1..864000 active seconds.");
        if (value.FlagsPerStore is < 1 or > 64 || value.GenerationInterval is < 1 or > 864000)
            errors.Add("AWD flags per store must be 1..64 and generation interval 1..864000.");
        if (value.RoundGapSeconds is < 0 or > 86400 || value.SubmissionTailSeconds is < 0 or > 86400
            || value.TotalRounds is <= 0 or > 1000000)
            errors.Add("AWD round gaps and submission tails must be 0..86400 seconds; total rounds must be 1..1000000.");
        if (CheckerIntervalSeconds is < 1 or > 86400 || value.FinalCheckBudgetSeconds is < 1 or > 300
            || value.RetryCount is < 0 or > 10 || value.RetryIntervalSeconds is < 1 or > 300
            || value.CallbackGraceSeconds is < 0 or > 300)
            errors.Add("AWD checker schedule or retry budget is outside its supported range.");
        if (new[] { value.OkRatio, value.RecoveringRatio, value.MumbleRatio, value.OfflineRatio,
                value.MumblePenaltyRatio, value.OfflinePenaltyRatio, value.RecoveringPenaltyRatio }
            .Any(ratio => ratio is < 0 or > 1))
            errors.Add("AWD status and penalty ratios must be between zero and one.");
        if (new[] { AttackPoints, DefensePoints, SlaPoints, value.MumblePenalty, value.OfflinePenalty,
                value.RecoveringPenalty, value.ConfidentialityPoints, value.MaximumRoundPenalty ?? 0 }
            .Any(points => points < 0 || points > ScoreValueLimits.MaximumConfiguredValue))
            errors.Add("AWD scoring values exceed the configured score limit.");
        if (value.MinimumTotalScore is < -ScoreValueLimits.MaximumConfiguredValue or > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add("AWD minimum total score exceeds the configured score limit.");
        if (value.MinimumAttackRatio is <= 0 or > 1 || value.ScarcityAlpha is <= 0 or > 10
            || value.MaximumRankMultiplier is < 1 or > 100 || value.ConsecutiveFailureMultiplier is < 1 or > 10
            || new[] { value.AttackWeight, value.DefenseWeight, value.SlaWeight }.Any(weight => weight is < 0 or > 100))
            errors.Add("AWD scoring curve or weight is outside its supported range.");
        if (AttackMode is AwdAttackRewardMode.SplitVictimDefensePool or AwdAttackRewardMode.Scarcity
            && value.AttackSettlement != AwdAttackSettlement.FlagExpiry)
            errors.Add("Capture-dependent AWD scoring is provisional until flag expiry.");
        if ((AttackMode == AwdAttackRewardMode.SplitVictimDefensePool) != (value.DefenseMode == AwdDefenseMode.SharedPool))
            errors.Add("AWD shared-pool attack and defense must be selected together.");
        return errors;
    }

    private static int LifetimeMaximum(AwdLifetimeUnit unit) => unit == AwdLifetimeUnit.Rounds ? 1000 : 864000;
}
