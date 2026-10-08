using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Worker.LiveSolo;

public sealed class LiveSoloRoundMessageHandler(ILiveSoloMatchStore matches, TimeProvider clock)
{
    public Task Handle(AdvanceLiveSoloRound message, CancellationToken ct) =>
        matches.TickAsync(message.RoundId, message.TimelineRevision, clock.GetUtcNow(), ct);
}
