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
    IReadOnlyList<CompetitionLifecycleAudit>? LifecycleAudits = null);

public sealed record LeaderboardTeamFact(
    Guid Id,
    string Name,
    bool IsBanned,
    bool IsDeleted,
    DateTimeOffset RegisteredAt = default);
public sealed record LeaderboardChallengeFact(Guid Id, string Direction, bool IsDeleted, string? ConfigurationJson = null);

public sealed record LeaderboardSubmissionFact(
    Guid SubmissionId,
    Guid TeamId,
    Guid? CompetitionChallengeId,
    SubmissionKind Kind,
    DateTimeOffset ReceivedAt,
    ScoringEvent Event,
    Guid? VictimTeamId = null);

public sealed record LeaderboardSystemFact(ScoringEvent Event, long CurrentValue = 0);

public interface IGameModeLeaderboardProjector
{
    GameMode Mode { get; }
    IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input);
}

public interface ILeaderboardProjectorCatalog
{
    IGameModeLeaderboardProjector Get(GameMode mode);
}

public sealed record LeaderboardProjectionResult(
    IReadOnlyList<LeaderboardEntry> Entries,
    IReadOnlyList<LeaderboardSubjectSummary> Subjects,
    IReadOnlyList<LeaderboardFirstBloodSummary> FirstBloods);

public interface ILeaderboardProjectionEngine
{
    LeaderboardProjectionResult Project(LeaderboardProjectionInput input);
}
