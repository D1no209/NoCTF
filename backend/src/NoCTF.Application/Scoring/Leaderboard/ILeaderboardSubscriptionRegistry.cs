namespace NoCTF.Application.Scoring.Leaderboard;

public interface ILeaderboardSubscriptionRegistry
{
    Task<bool> TouchAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken);

    Task<bool> HasActiveAsync(
        Guid competitionId,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken);
}
