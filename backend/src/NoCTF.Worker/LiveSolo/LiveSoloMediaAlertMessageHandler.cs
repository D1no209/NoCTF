using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloMediaAlertMessageHandler(PublishLiveSoloMediaAlert publisher)
{
    public Task Handle(LiveSoloMediaAlertCreated message, CancellationToken ct) => publisher.ExecuteAsync(message, ct);
}
