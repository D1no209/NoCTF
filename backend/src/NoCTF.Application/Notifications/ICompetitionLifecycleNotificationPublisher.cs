using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Notifications;

public interface ICompetitionLifecycleNotificationPublisher
{
    Task PublishAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
