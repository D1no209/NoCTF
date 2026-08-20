using System.Globalization;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Notifications;

public sealed record ScoreboardUpdated(
    Guid CompetitionId,
    string Version,
    string SchemaRevision,
    string ChallengeCatalogRevision)
{
    public static ScoreboardUpdated From(ScoreboardProjection projection) => new(
        projection.Snapshot.CompetitionId,
        projection.Snapshot.Version.ToString(CultureInfo.InvariantCulture),
        projection.Schema.Revision.ToString(CultureInfo.InvariantCulture),
        projection.ChallengeCatalog.Revision.ToString(CultureInfo.InvariantCulture));
}

public interface ILeaderboardRefreshPublisher
{
    Task PublishAsync(ScoreboardProjection projection, CancellationToken cancellationToken);
}
