using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardProjectionInput(
    Guid CompetitionId,
    GameMode Mode,
    IReadOnlyList<LeaderboardTeamFact> Teams,
    IReadOnlyList<LeaderboardSubmissionFact> Submissions,
    IReadOnlyList<LeaderboardSystemFact> SystemEvents,
    IReadOnlyList<LeaderboardChallengeFact>? Challenges = null,
    string? CompetitionConfigurationJson = null,
    DateTimeOffset? CompetitionStartTime = null,
    IReadOnlyList<CompetitionLifecycleTransition>? LifecycleAudits = null,
    IReadOnlyList<LeaderboardAwdRoundFact>? AwdRounds = null,
    DateTimeOffset? ProjectedAt = null);

public sealed record LeaderboardTeamFact(
    Guid Id,
    string Name,
    bool IsBanned,
    bool IsDeleted,
    DateTimeOffset RegisteredAt = default);
public sealed record LeaderboardChallengeFact(
    Guid Id,
    string Direction,
    string Title,
    bool IsDeleted,
    string? ConfigurationJson = null);

public sealed record LeaderboardSubmissionFact(
    Guid SubmissionId,
    Guid TeamId,
    Guid? CompetitionChallengeId,
    SubmissionKind Kind,
    DateTimeOffset ReceivedAt,
    ScoringEvent Event,
    Guid? VictimTeamId = null,
    string? SubmitterName = null,
    string? SubmittedFlag = null,
    long? HintCost = null);

public sealed record LeaderboardSystemFact(ScoringEvent Event, long CurrentValue = 0);

public sealed record LeaderboardAwdRoundFact(
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid RoundId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record GameModeLeaderboardProjection(
    IReadOnlyList<LeaderboardEntry> Entries,
    IReadOnlyList<LeaderboardTeamSeries> Series);

public interface IGameModeLeaderboardProjector
{
    GameMode Mode { get; }
    GameModeLeaderboardProjection Project(LeaderboardProjectionInput input);
}

public interface ILeaderboardProjectorCatalog
{
    IGameModeLeaderboardProjector Get(GameMode mode);
}

public sealed record LeaderboardProjectionResult(
    IReadOnlyList<LeaderboardEntry> Entries,
    IReadOnlyList<LeaderboardSubjectSummary> Subjects,
    IReadOnlyList<LeaderboardBloodSummary> Bloods)
{
    public IReadOnlyList<LeaderboardTeamSeries> Series { get; init; } = [];
    public IReadOnlyList<LeaderboardChallengeInfo> Challenges { get; init; } = [];
}

public interface ILeaderboardProjectionEngine
{
    LeaderboardProjectionResult Project(LeaderboardProjectionInput input);
}
