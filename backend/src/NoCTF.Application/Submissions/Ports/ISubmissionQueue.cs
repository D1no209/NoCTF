namespace NoCTF.Application.Submissions.Ports;

public sealed record ProcessFlagSubmission(Guid CompetitionId, Guid TeamId, Guid UserId, Guid SubmissionId);
public sealed record ProcessFixSubmission(Guid CompetitionId, Guid TeamId, Guid UserId, Guid SubmissionId);

/// <summary>Durably schedules submission processing without carrying secret content.</summary>
public interface ISubmissionQueue
{
    Task EnqueueAsync(ProcessFlagSubmission message, CancellationToken cancellationToken);
    Task EnqueueAsync(ProcessFixSubmission message, CancellationToken cancellationToken);
}
