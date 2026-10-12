using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Competitions.Awd;

/// <summary>Only scoring changes are permitted on already issued rules.</summary>
public sealed record AwdScoringPolicy
{
    public AwdAttackRewardMode AttackRewardMode { get; init; }
    public long AttackPoints { get; init; } = 50;
    public long VictimDefensePoolPoints { get; init; } = 100;
    public long ServiceHealthyPoints { get; init; } = 100;
    public AwdSlaSettlement SlaSettlement { get; init; }
    public AwdDefenseMode DefenseMode { get; init; }
    public AwdSlaBasis SlaBasis { get; init; }
    public decimal OkRatio { get; init; } = 1m;
    public decimal RecoveringRatio { get; init; } = 0.5m;
    public decimal MumbleRatio { get; init; }
    public decimal OfflineRatio { get; init; }
    public AwdPenaltyMode PenaltyMode { get; init; }
    public long MumblePenalty { get; init; }
    public long OfflinePenalty { get; init; }
    public long RecoveringPenalty { get; init; }
    public decimal MumblePenaltyRatio { get; init; }
    public decimal OfflinePenaltyRatio { get; init; }
    public decimal RecoveringPenaltyRatio { get; init; }
    public decimal ConsecutiveFailureMultiplier { get; init; } = 1m;
    public long? MaximumRoundPenalty { get; init; }
    public long ConfidentialityPoints { get; init; }
    public decimal MinimumAttackRatio { get; init; } = 0.1m;
    public decimal ScarcityAlpha { get; init; } = 0.705m;
    public decimal MaximumRankMultiplier { get; init; } = 2m;
    public AwdScoreComposition Composition { get; init; }
    public decimal AttackWeight { get; init; } = 1m;
    public decimal DefenseWeight { get; init; } = 1m;
    public decimal SlaWeight { get; init; } = 1m;
    public AwdAttackSettlement AttackSettlement { get; init; }

    public static AwdScoringPolicy From(AwdRoundRules rules) => new()
    {
        AttackRewardMode = rules.AttackRewardMode, AttackPoints = rules.AttackPoints,
        VictimDefensePoolPoints = rules.VictimDefensePoolPoints, ServiceHealthyPoints = rules.ServiceHealthyPoints,
        SlaSettlement = rules.Options.SlaSettlement,
        DefenseMode = rules.Options.DefenseMode,
        SlaBasis = rules.Options.SlaBasis,
        OkRatio = rules.Options.OkRatio,
        RecoveringRatio = rules.Options.RecoveringRatio,
        MumbleRatio = rules.Options.MumbleRatio,
        OfflineRatio = rules.Options.OfflineRatio,
        PenaltyMode = rules.Options.PenaltyMode,
        MumblePenalty = rules.Options.MumblePenalty,
        OfflinePenalty = rules.Options.OfflinePenalty,
        RecoveringPenalty = rules.Options.RecoveringPenalty,
        MumblePenaltyRatio = rules.Options.MumblePenaltyRatio,
        OfflinePenaltyRatio = rules.Options.OfflinePenaltyRatio,
        RecoveringPenaltyRatio = rules.Options.RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = rules.Options.ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = rules.Options.MaximumRoundPenalty,
        ConfidentialityPoints = rules.Options.ConfidentialityPoints,
        MinimumAttackRatio = rules.Options.MinimumAttackRatio,
        ScarcityAlpha = rules.Options.ScarcityAlpha,
        MaximumRankMultiplier = rules.Options.MaximumRankMultiplier,
        Composition = rules.Options.Composition,
        AttackWeight = rules.Options.AttackWeight,
        DefenseWeight = rules.Options.DefenseWeight,
        SlaWeight = rules.Options.SlaWeight,
        AttackSettlement = rules.Options.AttackSettlement,
    };

    // This is an in-memory projection copy. The issuance policy and all identities stay immutable.
    public AwdRoundRules Apply(AwdRoundRules rules) => new()
    {
        Id = rules.Id, CompetitionId = rules.CompetitionId, RoundId = rules.RoundId,
        CompetitionChallengeId = rules.CompetitionChallengeId, Revision = rules.Revision, EffectiveAt = rules.EffectiveAt,
        AttackRewardMode = AttackRewardMode, AttackPoints = AttackPoints,
        VictimDefensePoolPoints = VictimDefensePoolPoints, ServiceHealthyPoints = ServiceHealthyPoints,
        CheckerIntervalSeconds = rules.CheckerIntervalSeconds, EligibleTeamCount = rules.EligibleTeamCount,
        FlagHeader = rules.FlagHeader, FlagBodyTemplate = rules.FlagBodyTemplate, Stores = rules.Stores, Teams = rules.Teams,
        Options = rules.Options with
        {
            Preset = AwdScoringPreset.Custom,
            SlaSettlement = SlaSettlement,
            DefenseMode = DefenseMode,
            SlaBasis = SlaBasis,
            OkRatio = OkRatio,
            RecoveringRatio = RecoveringRatio,
            MumbleRatio = MumbleRatio,
            OfflineRatio = OfflineRatio,
            PenaltyMode = PenaltyMode,
            MumblePenalty = MumblePenalty,
            OfflinePenalty = OfflinePenalty,
            RecoveringPenalty = RecoveringPenalty,
            MumblePenaltyRatio = MumblePenaltyRatio,
            OfflinePenaltyRatio = OfflinePenaltyRatio,
            RecoveringPenaltyRatio = RecoveringPenaltyRatio,
            ConsecutiveFailureMultiplier = ConsecutiveFailureMultiplier,
            MaximumRoundPenalty = MaximumRoundPenalty,
            ConfidentialityPoints = ConfidentialityPoints,
            MinimumAttackRatio = MinimumAttackRatio,
            ScarcityAlpha = ScarcityAlpha,
            MaximumRankMultiplier = MaximumRankMultiplier,
            Composition = Composition,
            AttackWeight = AttackWeight,
            DefenseWeight = DefenseWeight,
            SlaWeight = SlaWeight,
            AttackSettlement = AttackSettlement,
        }
    };
}

[Table("awd_scoring_rule_changes")]
public sealed class AwdScoringRuleChange
{
    [Key] public Guid EventId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int Sequence { get; set; }
    public int FirstRound { get; set; }
    public int LastRound { get; set; }
    public AwdRuleChangeTiming Timing { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public AwdScoringPolicy Policy { get; set; } = new();
}
