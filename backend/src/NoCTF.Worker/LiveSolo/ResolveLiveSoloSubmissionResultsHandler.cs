using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Worker.LiveSolo;

public sealed class ResolveLiveSoloSubmissionResultsHandler(ILiveSoloMatchStore matches, TimeProvider clock)
{
    public Task Handle(ResolveLiveSoloSubmissionResults message, CancellationToken ct) => matches.ResolveAsync(message.RoundId, clock.GetUtcNow(), ct);
}
