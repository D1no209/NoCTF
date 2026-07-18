using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public interface IGameModeLeaderboardProjector
{
    GameMode Mode { get; }

    LeaderboardSnapshot Project(
        ScoringContext context,
        long projectionVersion,
        IReadOnlyList<IScoringStreamEvent> events);
}
