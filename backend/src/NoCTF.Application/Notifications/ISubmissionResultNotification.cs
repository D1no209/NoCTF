using NoCTF.Application.Submissions.Status;

namespace NoCTF.Application.Notifications;

/// <summary>Non-sensitive completion message; submitted contents are never included.</summary>
public sealed record SubmissionResultNotification(Guid UserId, SubmissionStatusView Result);

public interface ISubmissionResultNotification
{
    Task PublishAsync(SubmissionResultNotification notification, CancellationToken cancellationToken);
}
