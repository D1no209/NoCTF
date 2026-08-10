namespace NoCTF.Application.Authentication;

public sealed record AwdCheckerTokenRequest(
    string RunnerId,
    Guid RuntimeInstanceId,
    int Generation,
    long CheckerSequence,
    long ProcessingVersion,
    DateTimeOffset Deadline,
    DateTimeOffset IssuedAt);

public sealed record AwdpFixResultTokenRequest(
    string RunnerId,
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    int Generation,
    long RuntimeProcessingVersion,
    DateTimeOffset Deadline,
    DateTimeOffset IssuedAt);

public interface IRunnerScoringTokenIssuer
{
    string Issue(string runnerId, DateTimeOffset now);
    string IssueFixArchiveRead(string runnerId, Guid uploadId, Guid gameplayFactId, DateTimeOffset now);
    string IssueAwdChecker(AwdCheckerTokenRequest request);
    string IssueAwdpFixResult(AwdpFixResultTokenRequest request);
}
