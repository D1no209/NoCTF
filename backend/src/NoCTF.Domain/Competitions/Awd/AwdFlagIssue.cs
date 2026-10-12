using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Awd;

/// <summary>AWD-specific issuance identity and delivery state owned by a ChallengeFlag.</summary>
[Table("awd_flag_issues")]
public sealed class AwdFlagIssue : IConcurrencyTracked
{
    [Key] public Guid FlagId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid RoundId { get; set; }
    public int RoundNumber { get; set; }
    public Guid RulesId { get; set; }
    public Guid StoreId { get; set; }
    public long GenerationSlot { get; set; }
    public int Ordinal { get; set; }
    public int KeyVersion { get; set; } = 1;
    public int ProtocolVersion { get; set; } = 1;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string Digest { get; set; } = string.Empty;
    public AwdFlagIssueState State { get; set; }
    public AwdFlagDeliveryState DeliveryState { get; set; }
    public AwdFlagObligation Obligation { get; set; }
    public AwdCheckReason DeliveryReason { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long ActiveStartTicks { get; set; }
    public long? ActiveExpiryTicks { get; set; }
    public int? ExpiryRound { get; set; }
    public long? HistoricalActiveExpiryTicks { get; set; }
    public int? HistoricalExpiryRound { get; set; }
    public Guid? DeliveryRuntimeId { get; set; }
    [MaxLength(2048)] public string? PublicLocator { get; set; }
    [MaxLength(32768)] public string? ProtectedReadContext { get; set; }
}

/// <summary>A unique successful-capture identity, not a second submission or scoring record.</summary>
[Table("awd_capture_identities")]
public sealed class AwdCaptureIdentity
{
    public Guid CompetitionId { get; set; }
    public Guid FlagId { get; set; }
    public Guid AttackingTeamId { get; set; }
    public Guid GameplayFactId { get; set; }
}
