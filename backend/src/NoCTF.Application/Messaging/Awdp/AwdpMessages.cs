namespace NoCTF.Application.Messaging;

public sealed record CreateAwdpTarget(
    Guid SubmissionId,
    Guid CompetitionChallengeId,
    int Generation,
    long ProcessingVersion);

public sealed record RunAwdpFixVerification(
    Guid SubmissionId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion);

public sealed record CleanupAwdpTarget(
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion);
