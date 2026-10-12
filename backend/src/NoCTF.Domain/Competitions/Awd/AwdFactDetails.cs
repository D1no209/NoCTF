using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Competitions.Awd;

/// <summary>One current result and its evidence for an AWD GameplayFact.</summary>
[Table("awd_fact_details")]
public sealed class AwdFactDetails
{
    [Key] public Guid GameplayFactId { get; set; }
    public Guid? FlagId { get; set; }
    public Guid? GenerationRoundId { get; set; }
    public Guid? ReceivedRoundId { get; set; }
    public int? ReceivedRoundNumber { get; set; }
    public long? ReceivedActiveTicks { get; set; }
    public bool? ReceiptScorable { get; set; }
    public AwdAttackOutcome? AttackOutcome { get; set; }
    public Guid? RulesId { get; set; }
    public Guid? RuntimeId { get; set; }
    public AwdCheckExecution Execution { get; set; }
    public AwdSettlementState Settlement { get; set; }
    public AwdHealth? Health { get; set; }
    public AwdHealth? KnownTargetFailure { get; set; }
    public AwdCheckReason Reason { get; set; }
    public AwdProbeOutcome Connection { get; set; }
    public AwdCheckReason ConnectionReason { get; set; }
    public AwdProbeOutcome Functionality { get; set; }
    public AwdCheckReason FunctionalityReason { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset? ReceiptDeadline { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public bool FinalScheduledCheck { get; set; }
    public int Attempt { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset? AttemptStartedAt { get; set; }
    public DateTimeOffset? AttemptDeadline { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public long? SampleActiveTicks { get; set; }
    public long? SampleDeadlineActiveTicks { get; set; }
    public bool ManifestComplete { get; set; }
    public int RequiredFlags { get; set; }
    public int ReadableFlags { get; set; }
    public List<AwdFactFlagEvidence> Flags { get; set; } = [];
}

public enum AwdAttackOutcome : short
{
    Accepted, Invalid, Expired, Duplicate, OwnFlag, UnknownFlag, NotScorable, SystemError
}

public sealed class AwdFactFlagEvidence
{
    public Guid GameplayFactId { get; set; }
    public Guid FlagId { get; set; }
    public bool Historical { get; set; }
    public bool RequiresWrite { get; set; }
    public AwdFlagObligation Obligation { get; set; }
    public AwdProbeOutcome Write { get; set; }
    public AwdCheckReason WriteReason { get; set; }
    public AwdProbeOutcome Read { get; set; }
    public AwdCheckReason ReadReason { get; set; }
}
