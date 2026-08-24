using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public sealed record RunAwdpFixVerification(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CleanupAwdpTarget(
    Guid RuntimeInstanceId,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CompleteAwdpFixRecovery(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    string RunnerId,
    DateTimeOffset CleanedAt);

public sealed record ExpireAwdpFixVerification(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId);
