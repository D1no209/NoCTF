namespace NoCTF.Application.Messaging;

public sealed record EvaluateSubmission(Guid SubmissionId, long ProcessingVersion);
public sealed record ProjectLeaderboard(Guid CompetitionId);
public sealed record CleanupCompetitionRuntimes(Guid CompetitionId);
public sealed record ProvisionCompetitionRuntimes(Guid CompetitionId);
public sealed record SendEmailVerification(Guid UserId, string Token);
public sealed record DispatchRuntime(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record StopRuntime(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record DrainSubmissions(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    bool Rejudge,
    Guid? SubmissionId = null);
