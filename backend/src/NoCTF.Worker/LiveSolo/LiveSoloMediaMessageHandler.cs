using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloMediaMessageHandler(ILiveSoloMediaStore store)
{
    public Task Handle(RefreshLiveSoloMedia message, CancellationToken ct) => store.RefreshAsync(message, ct);
}
