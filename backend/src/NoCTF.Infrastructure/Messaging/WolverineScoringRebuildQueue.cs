using NoCTF.Application.Scoring.Ports;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed record RebuildCompetitionScoring(Guid CompetitionId, ScoringRebuildReason Reason);

public sealed class WolverineScoringRebuildQueue(IMessageBus bus) : IScoringRebuildQueue
{
    public async Task EnqueueAsync(Guid competitionId, ScoringRebuildReason reason, CancellationToken cancellationToken) =>
        await bus.SendAsync(new RebuildCompetitionScoring(competitionId, reason));
}
