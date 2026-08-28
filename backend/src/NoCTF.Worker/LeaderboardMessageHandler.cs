using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Worker;

public sealed class LeaderboardMessageHandler(ILeaderboardCache leaderboard)
{
    public Task Handle(
        ProjectLeaderboard message,
        CancellationToken cancellationToken) =>
        leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);
}
