namespace NoCTF.Application.Authentication;

public interface IRunnerScoringTokenIssuer
{
    string Issue(string runnerId, DateTimeOffset now);
    string IssueFixArchiveRead(string runnerId, Guid uploadId, Guid submissionId, DateTimeOffset now);
}
