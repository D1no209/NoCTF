using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public sealed record RunAwdpFixVerification(
    Guid SubmissionId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion,
    long RuntimeProcessingVersion,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CleanupAwdpTarget(
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record ExpireAwdpFixVerification(
    Guid SubmissionId,
    Guid RuntimeInstanceId,
    int Generation,
    long ProcessingVersion,
    long RuntimeProcessingVersion,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId);
