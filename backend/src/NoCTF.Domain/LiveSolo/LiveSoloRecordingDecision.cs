using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloRecordingAction : short { Hold, ReleaseHold, Publish, Withdraw }

/// <summary>Immutable decision retained after the recording's retention cleanup.</summary>
public sealed class LiveSoloRecordingDecision
{
    public Guid Id { get; set; }
    public Guid RecordingId { get; set; }
    public Guid MediaSessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public LiveSoloRecordingAction Action { get; set; }
    [Required, MaxLength(4000)] public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public bool PreviousHold { get; set; }
    public bool Hold { get; set; }
    public bool PreviousPublished { get; set; }
    public bool Published { get; set; }
}
