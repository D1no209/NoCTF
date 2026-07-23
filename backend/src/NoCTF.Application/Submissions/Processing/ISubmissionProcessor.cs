namespace NoCTF.Application.Submissions.Processing;

public interface ISubmissionProcessor
{
    Task ProcessAsync(Guid submissionId, long processingVersion, CancellationToken cancellationToken);
}
