using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardProjectionInput(
    Guid CompetitionId,
    GameMode Mode,
    IReadOnlyList<LeaderboardTeamFact> Teams,
    IReadOnlyList<LeaderboardSubmissionFact> Submissions,
    IReadOnlyList<LeaderboardSystemFact> SystemEvents);

public sealed record LeaderboardTeamFact(Guid Id, string Name, bool IsBanned, bool IsDeleted);

public sealed record LeaderboardSubmissionFact(
    Guid SubmissionId,
    Guid TeamId,
    Guid? ChallengeId,
    SubmissionKind Kind,
    DateTimeOffset ReceivedAt,
    ScoringEvent Event);

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
