using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloCaptureMessageHandler(ILiveSoloCaptureStore store)
{
    public Task Handle(SnapshotLiveSoloResult message,CancellationToken ct)=>store.SnapshotResultAsync(message.MatchId,ct);
    public Task Handle(AdvanceLiveSoloCapture message, CancellationToken ct) => store.AdvanceAsync(message.MediaSessionId, ct);
    public Task Handle(PruneLiveSoloCapture message, CancellationToken ct) => store.PruneAsync(message.MediaSessionId, ct);
    public Task Handle(RemoveLiveSoloCaptureFiles message, CancellationToken ct) => store.FinishPruneAsync(message.CaptureId, ct);
    public Task Handle(RemoveLiveSoloRecordingRaw message,CancellationToken ct)=>store.RemoveRecordingRawAsync(message.RecordingId,ct);
}
