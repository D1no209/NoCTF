using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloProgramAction : short { ReconcileExport, RetryImport, Rotate }
public sealed class LiveSoloProgramDecision
{
    public Guid Id { get; set; }
    public Guid ProgramCaptureId { get; set; }
    public Guid MediaSessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public LiveSoloProgramAction Action { get; set; }
    [Required,MaxLength(4000)] public string Reason { get; set; }="";
    public DateTimeOffset OccurredAt { get; set; }
    public LiveSoloCaptureState PreviousState { get; set; }
    public LiveSoloCaptureState State { get; set; }
}
