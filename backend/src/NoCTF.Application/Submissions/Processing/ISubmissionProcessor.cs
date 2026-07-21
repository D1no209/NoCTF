namespace NoCTF.Application.Submissions.Processing;

public interface ISubmissionProcessor
{
    Task ProcessAsync(Guid submissionId, CancellationToken cancellationToken);
}
