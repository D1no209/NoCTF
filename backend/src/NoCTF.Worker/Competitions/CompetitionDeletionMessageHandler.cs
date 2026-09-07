using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Competitions.Management;

namespace NoCTF.Worker.Competitions;

public sealed class CompetitionDeletionMessageHandler(CompetitionReadModelCache readModels)
{
    public Task Handle(InvalidateDeletedCompetitionReadModels message, CancellationToken ct) =>
        readModels.InvalidateAsync(message.CompetitionId, ct);
}
