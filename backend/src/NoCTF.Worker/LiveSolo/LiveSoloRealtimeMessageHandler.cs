using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloRealtimeMessageHandler(ILiveSoloRealtimePublisher publisher,NoCTF.Application.LiveSolo.Media.ILiveSoloCaptureStore captures)
{
    public async Task Handle(LiveSoloMatchChanged change, CancellationToken ct)
    {
        await captures.SnapshotResultAsync(change.MatchId,ct);
        await publisher.PublishAsync(change,ct);
    }
}
