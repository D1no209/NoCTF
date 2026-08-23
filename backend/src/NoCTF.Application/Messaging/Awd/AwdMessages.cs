using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;

namespace NoCTF.Application.Messaging;

public sealed record AdvanceAwdRound(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset At);

public sealed record GenerateAwdFlags(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    AwdRoundSpecificationId Round,
    DateTimeOffset ValidStart,
    DateTimeOffset ValidUntil);

public sealed record InjectAwdFlag(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    Guid ChallengeFlagId,
    int Generation,
    DateTimeOffset ValidUntil,
    string RunnerPool,
    string RunnerId,
    int FailedAttempts = 0) : IRunnerNodeMessage;

public sealed record AwdFlagInjectionFailed(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid RuntimeInstanceId,
    Guid ChallengeFlagId,
    int Generation,
    DateTimeOffset OccurredAt);

public sealed record RunAwdChecker(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    int Generation,
    long CheckerSequence,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record DispatchAwdCheckers(
    DateTimeOffset At,
    Guid? AfterRuntimeInstanceId = null);

public sealed record AwdCheckerCallbackMissing(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid RuntimeInstanceId,
    int Generation,
    long CheckerSequence,
    DateTimeOffset OccurredAt);
