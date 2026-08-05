using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Events;

namespace NoCTF.Worker;

public sealed class CompetitionEventMessageHandlers(
    RedisCompetitionEventRefreshPublisher publisher)
{
    public Task Handle(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken) =>
        publisher.PublishAsync(message, cancellationToken);
}
