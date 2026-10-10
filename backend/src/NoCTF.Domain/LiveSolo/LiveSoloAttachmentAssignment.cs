namespace NoCTF.Domain.LiveSolo;

/// <summary>One immutable random attachment choice per team and RoundQuestion.</summary>
public sealed class LiveSoloAttachmentAssignment
{
    public Guid RoundQuestionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid AttachmentId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}
