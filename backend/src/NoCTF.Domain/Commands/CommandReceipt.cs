using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Commands;

public enum ReplayOperation : short
{
    FlagSubmission,
    ManualAdjustment,
    PatchUpload,
    RuntimeMutation,
    AdminRuntimeMutation,
    TemplateTestRuntimeMutation,
    AwdpDefenseTarget,
    PatchVerificationTarget
}

[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(ReplayOperation), "CommandReceipt")]
public abstract class CommandReceipt
{
    protected CommandReceipt(ReplayOperation operation) => Operation = operation;

    public Guid Id { get; set; }
    public ReplayOperation Operation { get; private set; }
    public Guid UserId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ResourceId { get; set; }
    public byte[] InputFingerprint { get; set; } = [];
    public short ResultState { get; set; }
    public Guid? PrimaryResultId { get; set; }
    public Guid? SecondaryResultId { get; set; }
    public RuntimeState? RuntimeState { get; set; }
    public GameplayFactState? GameplayFactState { get; set; }
    public DateTimeOffset? ResultOccurredAt { get; set; }
    public DateTimeOffset CommittedAt { get; set; }
    public List<CommandReceiptGameplayFactResult> GameplayFactResults { get; set; } = [];
}

public sealed class CommandReceiptGameplayFactResult
{
    public Guid CommandReceiptId { get; set; }
    public int Position { get; set; }
    public short State { get; set; }
    public Guid? GameplayFactId { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}
