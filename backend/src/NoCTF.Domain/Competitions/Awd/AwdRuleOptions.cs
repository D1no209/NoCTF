using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Competitions.Awd;

public enum AwdLifetimeUnit : short { Rounds, ActiveSeconds }
public enum AwdGenerationPolicy : short { EveryRound, EveryNRounds, ActiveInterval }
public enum AwdScoringPreset : short { Fixed, SharedPool, Scarcity, TargetRank, SlaMultiplier, Custom }
public enum AwdDefenseMode : short { FirstLeak, PerCapture, SharedPool }
public enum AwdSlaBasis : short { StatusRatio, ReadableFlags }
public enum AwdSlaSettlement : short { FinalScheduledCheck, SampleAverage, TimeWeighted }
public enum AwdPenaltyMode : short { SlaRewardOnly, Fixed, Proportional, Combined }
public enum AwdScoreComposition : short { Additive, Weighted, PositiveSlaMultiplier }
public enum AwdAttackSettlement : short { Immediate, RoundEnd, FlagExpiry }
public enum AwdFlagDeliveryMode : short { RunnerCommand, BusinessChecker }
public enum AwdFlagIssueState : short { Planned, Issued, Revoked }
public enum AwdFlagDeliveryState : short { Pending, Running, Succeeded, TargetFailed, Unconfirmed, PlatformFailed }
public enum AwdRuleChangeTiming : short { NextRound, Immediately, RecalculateHistory }

/// <summary>Relational AWD policy values. All values are resolved before an issuance or check.</summary>
public sealed record AwdRuleOptions
{
    public AwdLifetimeUnit FlagLifetimeUnit { get; init; }
    public int FlagLifetime { get; init; } = 5;
    public bool LinkHistoricalWindow { get; init; } = true;
    public AwdLifetimeUnit HistoricalLifetimeUnit { get; init; }
    public int HistoricalLifetime { get; init; } = 5;
    public AwdGenerationPolicy GenerationPolicy { get; init; }
    public int GenerationInterval { get; init; } = 1;
    public int FlagsPerStore { get; init; } = 1;
    public int RoundGapSeconds { get; init; }
    public int? TotalRounds { get; init; }
    public int SubmissionTailSeconds { get; init; }
    public int FinalCheckBudgetSeconds { get; init; } = 30;
    public int RetryCount { get; init; } = 2;
    public int RetryIntervalSeconds { get; init; } = 5;
    public int CallbackGraceSeconds { get; init; } = 30;
    public AwdSlaSettlement SlaSettlement { get; init; }
    public AwdScoringPreset Preset { get; init; }
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
    public long? MinimumTotalScore { get; init; }
    public long ConfidentialityPoints { get; init; }
    public decimal MinimumAttackRatio { get; init; } = 0.1m;
    public decimal ScarcityAlpha { get; init; } = 0.705m;
    public decimal MaximumRankMultiplier { get; init; } = 2m;
    public AwdScoreComposition Composition { get; init; }
    public decimal AttackWeight { get; init; } = 1m;
    public decimal DefenseWeight { get; init; } = 1m;
    public decimal SlaWeight { get; init; } = 1m;
    public AwdAttackSettlement AttackSettlement { get; init; }
}

public sealed class AwdFlagStore
{
    public Guid ChallengeId { get; set; }
    public Guid Id { get; set; }
    public int Position { get; set; }
    [MaxLength(64)] public string Name { get; set; } = "main";
    [MaxLength(64)] public string? TargetServiceName { get; set; }
    public AwdFlagDeliveryMode DeliveryMode { get; set; }
    [MaxLength(8192)] public string? InjectionCommand { get; set; }
    public int InjectionTimeoutSeconds { get; set; } = 30;
    public bool PreservesHistoricalFlags { get; set; } = true;
}

/// <summary>Scoring and issuance overrides belong to the competition instance, not the bank template.</summary>
public sealed class AwdStoreRule
{
    public Guid CompetitionChallengeId { get; set; }
    public Guid StoreId { get; set; }
    public decimal Weight { get; set; } = 1m;
    public int? FlagsPerIssue { get; set; }
}
