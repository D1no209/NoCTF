namespace NoCTF.Application.Authentication;

public sealed record AwdCheckerTokenRequest(
    string RunnerId,
    Guid RuntimeInstanceId,
    int Generation,
    long CheckerSequence,
    long ProcessingVersion,
    DateTimeOffset Deadline,
    DateTimeOffset IssuedAt);

public interface IRunnerScoringTokenIssuer
{
    string Issue(string runnerId, DateTimeOffset now);
    string IssueFixArchiveRead(string runnerId, Guid uploadId, Guid submissionId, DateTimeOffset now);
    string IssueAwdChecker(AwdCheckerTokenRequest request);
}
