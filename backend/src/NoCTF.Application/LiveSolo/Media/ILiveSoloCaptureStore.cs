namespace NoCTF.Application.LiveSolo.Media;

public sealed record AdvanceLiveSoloCapture(Guid MediaSessionId);
public interface ILiveSoloCaptureStore
{
    Task EnsureAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task AdvanceAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task PruneAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task FinishPruneAsync(Guid captureId, CancellationToken cancellationToken);
    Task RemoveRecordingRawAsync(Guid recordingId,CancellationToken cancellationToken);
    Task<NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure?> RecoverRecordingAsync(ChangeLiveSoloRecording command,CancellationToken cancellationToken);
}

public sealed record LiveSoloCapturedSegment(long Sequence, string FileName, TimeSpan Duration);
public interface ILiveSoloCaptureFiles
{
    Task<IReadOnlyList<LiveSoloCapturedSegment>> SegmentsAsync(Guid captureId, CancellationToken cancellationToken);
    Task<Stream?> OpenAsync(Guid captureId, string fileName, CancellationToken cancellationToken);
    Task RemoveAsync(Guid captureId, CancellationToken cancellationToken);
    Task<long?> RecordingLengthAsync(Guid captureId,CancellationToken cancellationToken);
}
public sealed record PruneLiveSoloCapture(Guid MediaSessionId);
public sealed record RemoveLiveSoloCaptureFiles(Guid CaptureId);
public sealed record RemoveLiveSoloRecordingRaw(Guid RecordingId);
