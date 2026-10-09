using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloRealtimeMessageHandler(ILiveSoloRealtimePublisher publisher)
{
    public async Task Handle(LiveSoloMatchChanged change, CancellationToken ct)
    {
        await publisher.PublishAsync(change,ct);
    }
}
