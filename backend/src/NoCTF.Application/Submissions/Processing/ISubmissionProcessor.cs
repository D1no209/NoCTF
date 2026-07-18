namespace NoCTF.Application.Submissions.Processing;

public interface ISubmissionProcessor
{
    Task ProcessFlagAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken);
    Task ProcessFixAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken);
}
