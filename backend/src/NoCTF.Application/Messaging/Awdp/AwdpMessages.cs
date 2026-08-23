using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public sealed record RunAwdpFixVerification(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    int Generation,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CleanupAwdpTarget(
    Guid RuntimeInstanceId,
    int Generation,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CompleteAwdpFixRecovery(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    int Generation,
    string RunnerPool,
    string RunnerId,
    DateTimeOffset CleanedAt);

public sealed record ExpireAwdpFixVerification(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    int Generation,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId);
