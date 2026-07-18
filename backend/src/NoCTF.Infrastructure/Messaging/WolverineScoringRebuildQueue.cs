using NoCTF.Application.Scoring.Ports;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed record RebuildCompetitionScoring(Guid CompetitionId, string Reason);

public sealed class WolverineScoringRebuildQueue(IMessageBus bus) : IScoringRebuildQueue
{
    public async Task EnqueueAsync(Guid competitionId, string reason, CancellationToken cancellationToken) =>
        await bus.SendAsync(new RebuildCompetitionScoring(competitionId, reason));
}
