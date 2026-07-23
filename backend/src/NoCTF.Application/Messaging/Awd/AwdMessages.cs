using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public sealed record AdvanceAwdRound(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    int Round,
    long ProcessingVersion);

public sealed record GenerateAwdFlags(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    int Round,
    long ProcessingVersion);

public sealed record InjectAwdFlag(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    Guid ChallengeFlagId,
    int Generation,
    long ProcessingVersion,
    DateTimeOffset ValidUntil,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;

public sealed record RunAwdChecker(
    Guid RuntimeInstanceId,
    Guid CompetitionChallengeId,
    int Generation,
    long CheckerSequence,
    long ProcessingVersion,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId) : IRunnerNodeMessage;
