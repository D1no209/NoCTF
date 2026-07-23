namespace NoCTF.Application.Messaging;

public sealed record EvaluateSubmission(Guid SubmissionId, long ProcessingVersion);

public sealed record DrainSubmissions(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    bool Rejudge,
    Guid? SubmissionId = null);
