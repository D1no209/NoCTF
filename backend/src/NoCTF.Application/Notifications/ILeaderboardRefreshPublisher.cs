namespace NoCTF.Application.Notifications;

public interface ILeaderboardRefreshPublisher
{
    Task PublishAsync(Guid competitionId, DateTimeOffset generatedAt, CancellationToken cancellationToken);
}
