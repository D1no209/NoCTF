using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloMediaState : short { Preparing, Ready, Rotating, Stopping, Stopped, Failed }
public enum LiveSoloScreenState : short { Disconnected, Connected, Sharing }
public enum LiveSoloRecordingState : short { Pending, Recording, Finalizing, Completed, Failed, Deleting, Starting, RequiresReview }
public enum LiveSoloRecordingFailure : short { CapacityUnavailable, StartUncertain, ExportFailed, ExportTooLarge, ArchiveCapacityUnavailable, SourceUnavailable }

public sealed class LiveSoloMediaSession : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid Generation { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string RoomIdentity { get; set; } = string.Empty;
    public LiveSoloMediaState State { get; set; }
    public bool ParticipantsMayViewOpponents { get; set; }
    public bool RecordingEnabled { get; set; }
    public int PublicDelaySeconds { get; set; }
    public int RecordingRetentionDays { get; set; } = 30;
    public Guid? CurrentProgramCaptureId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public List<LiveSoloMediaParticipant> Participants { get; set; } = [];
}

public sealed class LiveSoloMediaParticipant
{
    public Guid MediaSessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid TeamId { get; set; }
    public LiveSoloSide Side { get; set; }
    [MaxLength(128)] public string Identity { get; set; } = string.Empty;
    [MaxLength(128)] public string? ScreenTrackId { get; set; }
    public LiveSoloScreenState ScreenState { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class LiveSoloProgramSegment
{
    public Guid Id { get; set; }
    public Guid MediaSessionId { get; set; }
    public long Sequence { get; set; }
    public Guid ProgramCaptureId { get; set; }
    public Guid FrameId { get; set; }
    public Guid FileId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public DateTimeOffset PublicAt { get; set; }
    public DateTimeOffset RemoveAfter { get; set; }
}

public sealed class LiveSoloRecording : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MediaSessionId { get; set; }
    public Guid? RoundId { get; set; }
    public Guid UserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string? EgressId { get; set; }
    public LiveSoloRecordingState State { get; set; }
    public LiveSoloRecordingFailure? Failure { get; set; }
    [MaxLength(128)] public string VideoTrackId { get; set; } = string.Empty;
    public int Chunk { get; set; }
    public long ReservedBytes { get; set; }
    public Guid? VideoPolicyStamp {get;set;}
    public int? VideoMaximumWidth {get;set;}
    public int? VideoMaximumHeight {get;set;}
    public int? VideoMaximumFramesPerSecond {get;set;}
    public int? VideoBitrateBitsPerSecond {get;set;}
    public DateTimeOffset? RawRemovedAt { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public Guid? FileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset KeepUntil { get; set; }
    public bool DisputeHold { get; set; }
    public bool Published { get; set; }
}
