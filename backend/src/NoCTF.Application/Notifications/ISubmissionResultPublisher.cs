using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Notifications;

public interface ISubmissionResultPublisher
{
    Task PublishAsync(Guid userId, SubmissionStatusView result, CancellationToken cancellationToken);
}

public interface ILeaderboardPublisher
{
    Task PublishAsync(Guid competitionId, long projectionVersion, CancellationToken cancellationToken);
}
