using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Identity;

public enum UserAccountLifecycleAction : short
{
    Banned,
    Disabled,
    Anonymized,
    PhysicallyDeleted
}

public sealed class UserAccountLifecycleAudit
{
    [Key]
    public Guid Id { get; set; }
    public Guid TargetUserId { get; set; }
    [MaxLength(64)]
    public string TargetUserName { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public UserAccountLifecycleAction Action { get; set; }
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
