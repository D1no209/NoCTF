using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Awd;

[Table("awd_rounds")]
public sealed class AwdRound : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public int Number { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public long ActiveStartTicks { get; set; }
    public long ActiveEndTicks { get; set; }
    public int GapSeconds { get; set; }
    public int SubmissionTailSeconds { get; set; }
    public bool Missed { get; set; }
    public bool Partial { get; set; }
    public List<AwdRoundTieBreak> TieBreakers { get; set; } = [];
}

/// <summary>Immutable resolved policy for a question in a round, shared by its flags and checks.</summary>
[Table("awd_round_rules")]
public sealed class AwdRoundRules
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid RoundId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public AwdAttackRewardMode AttackRewardMode { get; set; }
    public long AttackPoints { get; set; }
    public long VictimDefensePoolPoints { get; set; }
    public long ServiceHealthyPoints { get; set; }
    public int CheckerIntervalSeconds { get; set; }
    public AwdRuleOptions Options { get; set; } = new();
    public int EligibleTeamCount { get; set; }
    [MaxLength(64)] public string FlagHeader { get; set; } = "flag";
    [MaxLength(512)] public string FlagBodyTemplate { get; set; } = "[SECURE]";
    public List<AwdRoundStoreBudget> Stores { get; set; } = [];
    public List<AwdRoundTeamEligibility> Teams { get; set; } = [];
}

public sealed class AwdRoundStoreBudget
{
    public Guid RulesId { get; set; }
    public Guid StoreId { get; set; }
    [MaxLength(64)] public string Name { get; set; } = "main";
    public decimal Weight { get; set; }
    public int FlagsPerIssue { get; set; }
    public AwdFlagDeliveryMode DeliveryMode { get; set; }
    [MaxLength(64)] public string? TargetServiceName { get; set; }
    [MaxLength(8192)] public string? InjectionCommand { get; set; }
    public int InjectionTimeoutSeconds { get; set; } = 30;
    public bool PreservesHistoricalFlags { get; set; }
}

public sealed class AwdRoundTeamEligibility
{
    public Guid RulesId { get; set; }
    public Guid TeamId { get; set; }
    public bool Competitive { get; set; } = true;
    public int? SettledRank { get; set; }
}
