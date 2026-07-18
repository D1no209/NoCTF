using NoCTF.Application.Scoring.Ports;
using NoCTF.Application.Scoring.Rebuild;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Worker.Scoring;

public sealed class RebuildCompetitionScoringHandler(RebuildScoring rebuild)
{
    public Task Handle(RebuildCompetitionScoring message, CancellationToken cancellationToken) =>
        rebuild.ExecuteAsync(message.CompetitionId, cancellationToken);
}
