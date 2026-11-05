using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Challenges.Capabilities;

namespace NoCTF.Application.Messaging;

public sealed record StartFixExecution(
    Guid GameplayFactId,
    Guid RuntimeInstanceId);

public sealed record RunFixExecution(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId) : IRunnerNodeMessage;

public sealed record CleanupFixTarget(
    Guid RuntimeInstanceId,
    string RunnerId) : IRunnerNodeMessage;

public sealed record RecoverFixExecution(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    string RunnerId,
    DateTimeOffset CleanedAt);

public sealed record ExpireFixExecution(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId);

public sealed record CompleteFixExecutionWork(Guid GameplayFactId, Guid RuntimeInstanceId, FixWorkCompletion Result, DateTimeOffset OccurredAt);
public sealed record FinalizeFixExecution(Guid GameplayFactId, Guid RuntimeInstanceId);
public sealed record RecoverFixExecutions;
