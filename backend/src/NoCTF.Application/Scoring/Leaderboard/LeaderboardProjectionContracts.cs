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
    DateTimeOffset? CompetitionStartTime = null);

public sealed record LeaderboardTeamFact(Guid Id, string Name, bool IsBanned, bool IsDeleted);
public sealed record LeaderboardChallengeFact(Guid Id, string Direction, bool IsDeleted, string? ConfigurationJson = null);

public sealed record LeaderboardSubmissionFact(
    Guid SubmissionId,
    Guid TeamId,
    Guid? ChallengeId,
    SubmissionKind Kind,
    DateTimeOffset ReceivedAt,
    ScoringEvent Event,
    Guid? SubjectTeamId = null,
    Guid? VictimTeamId = null,
    Guid? ServiceId = null,
    long? ControlIntervalSeconds = null,
    Guid? StageId = null);

public sealed record LeaderboardSystemFact(ScoringEvent Event);

public interface IGameModeLeaderboardProjector
{
    GameMode Mode { get; }
    IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input);
}

public interface ILeaderboardProjectorCatalog
{
    IGameModeLeaderboardProjector Get(GameMode mode);
}
