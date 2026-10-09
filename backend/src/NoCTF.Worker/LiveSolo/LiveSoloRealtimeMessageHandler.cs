using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloRealtimeMessageHandler(ILiveSoloRealtimePublisher publisher)
{
    public Task Handle(LiveSoloMatchChanged change, CancellationToken ct) => publisher.PublishAsync(change, ct);
}
