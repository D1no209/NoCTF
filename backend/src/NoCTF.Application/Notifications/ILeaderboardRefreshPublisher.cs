using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Notifications;

public sealed record ScoreboardUpdated(
    Guid CompetitionId,
    long Version,
    long SchemaRevision,
    long ChallengeCatalogRevision);

public interface ILeaderboardRefreshPublisher
{
    Task PublishAsync(ScoreboardProjection projection, CancellationToken cancellationToken);
}
