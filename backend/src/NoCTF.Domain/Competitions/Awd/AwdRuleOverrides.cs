namespace NoCTF.Domain.Competitions.Awd;

/// <summary>Nullable question overrides; an explicit zero never means inheritance.</summary>
public sealed record AwdRuleOverrides
{
    public AwdLifetimeUnit? FlagLifetimeUnit { get; init; }
    public int? FlagLifetime { get; init; }
    public bool? LinkHistoricalWindow { get; init; }
    public AwdLifetimeUnit? HistoricalLifetimeUnit { get; init; }
    public int? HistoricalLifetime { get; init; }
    public AwdGenerationPolicy? GenerationPolicy { get; init; }
    public int? GenerationInterval { get; init; }
    public int? FlagsPerStore { get; init; }
    public int? FinalCheckBudgetSeconds { get; init; }
    public int? RetryCount { get; init; }
    public int? RetryIntervalSeconds { get; init; }
    public int? CallbackGraceSeconds { get; init; }
    public AwdSlaSettlement? SlaSettlement { get; init; }
    public AwdScoringPreset? Preset { get; init; }
    public AwdDefenseMode? DefenseMode { get; init; }
    public AwdSlaBasis? SlaBasis { get; init; }
    public decimal? OkRatio { get; init; }
    public decimal? RecoveringRatio { get; init; }
    public decimal? MumbleRatio { get; init; }
    public decimal? OfflineRatio { get; init; }
    public AwdPenaltyMode? PenaltyMode { get; init; }
    public long? MumblePenalty { get; init; }
    public long? OfflinePenalty { get; init; }
    public long? RecoveringPenalty { get; init; }
    public decimal? MumblePenaltyRatio { get; init; }
    public decimal? OfflinePenaltyRatio { get; init; }
    public decimal? RecoveringPenaltyRatio { get; init; }
    public decimal? ConsecutiveFailureMultiplier { get; init; }
    public long? MaximumRoundPenalty { get; init; }
    public long? ConfidentialityPoints { get; init; }
    public decimal? MinimumAttackRatio { get; init; }
    public decimal? ScarcityAlpha { get; init; }
    public decimal? MaximumRankMultiplier { get; init; }
    public AwdAttackSettlement? AttackSettlement { get; init; }

    public AwdRuleOptions Resolve(AwdRuleOptions defaults) => defaults with
    {
        FlagLifetimeUnit = FlagLifetimeUnit ?? defaults.FlagLifetimeUnit,
        FlagLifetime = FlagLifetime ?? defaults.FlagLifetime,
        LinkHistoricalWindow = LinkHistoricalWindow ?? defaults.LinkHistoricalWindow,
        HistoricalLifetimeUnit = HistoricalLifetimeUnit ?? defaults.HistoricalLifetimeUnit,
        HistoricalLifetime = HistoricalLifetime ?? defaults.HistoricalLifetime,
        GenerationPolicy = GenerationPolicy ?? defaults.GenerationPolicy,
        GenerationInterval = GenerationInterval ?? defaults.GenerationInterval,
        FlagsPerStore = FlagsPerStore ?? defaults.FlagsPerStore,
        FinalCheckBudgetSeconds = FinalCheckBudgetSeconds ?? defaults.FinalCheckBudgetSeconds,
        RetryCount = RetryCount ?? defaults.RetryCount,
        RetryIntervalSeconds = RetryIntervalSeconds ?? defaults.RetryIntervalSeconds,
        CallbackGraceSeconds = CallbackGraceSeconds ?? defaults.CallbackGraceSeconds,
        SlaSettlement = SlaSettlement ?? defaults.SlaSettlement,
        Preset = Preset ?? defaults.Preset,
        DefenseMode = DefenseMode ?? defaults.DefenseMode,
        SlaBasis = SlaBasis ?? defaults.SlaBasis,
        OkRatio = OkRatio ?? defaults.OkRatio,
        RecoveringRatio = RecoveringRatio ?? defaults.RecoveringRatio,
        MumbleRatio = MumbleRatio ?? defaults.MumbleRatio,
        OfflineRatio = OfflineRatio ?? defaults.OfflineRatio,
        PenaltyMode = PenaltyMode ?? defaults.PenaltyMode,
        MumblePenalty = MumblePenalty ?? defaults.MumblePenalty,
        OfflinePenalty = OfflinePenalty ?? defaults.OfflinePenalty,
        RecoveringPenalty = RecoveringPenalty ?? defaults.RecoveringPenalty,
        MumblePenaltyRatio = MumblePenaltyRatio ?? defaults.MumblePenaltyRatio,
        OfflinePenaltyRatio = OfflinePenaltyRatio ?? defaults.OfflinePenaltyRatio,
        RecoveringPenaltyRatio = RecoveringPenaltyRatio ?? defaults.RecoveringPenaltyRatio,
        ConsecutiveFailureMultiplier = ConsecutiveFailureMultiplier ?? defaults.ConsecutiveFailureMultiplier,
        MaximumRoundPenalty = MaximumRoundPenalty ?? defaults.MaximumRoundPenalty,
        ConfidentialityPoints = ConfidentialityPoints ?? defaults.ConfidentialityPoints,
        MinimumAttackRatio = MinimumAttackRatio ?? defaults.MinimumAttackRatio,
        ScarcityAlpha = ScarcityAlpha ?? defaults.ScarcityAlpha,
        MaximumRankMultiplier = MaximumRankMultiplier ?? defaults.MaximumRankMultiplier,
        AttackSettlement = AttackSettlement ?? defaults.AttackSettlement
    };
}
