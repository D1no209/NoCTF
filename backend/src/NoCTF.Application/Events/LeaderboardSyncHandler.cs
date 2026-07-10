using NoCTF.Application.Leaderboard;

namespace NoCTF.Application.Events;

/// <summary>
/// Handles post-solve side effects: updates Redis leaderboard cache and fires SignalR notifications.
/// Called by CtfGameMode (and other game modes) after a successful flag submission.
/// </summary>
public class LeaderboardSyncHandler(
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier) : ISubmissionEventHandler
{
    public async Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
    {
        // 1. Notify flag solved (first-blood, etc.)
        await hubNotifier.NotifyFlagSolvedAsync(
            solvedEvent.CompetitionId,
            solvedEvent.ChallengeId,
            solvedEvent.ChallengeName,
            solvedEvent.TeamId,
            solvedEvent.TeamName,
            solvedEvent.IsFirstBlood,
            ct);

        // 2. Recalculate full leaderboard from DB
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(solvedEvent.CompetitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(solvedEvent.CompetitionId, ct);

        // 3. Update Redis cache
        await leaderboardCache.UpdateAsync(solvedEvent.CompetitionId, entries, cacheVersion, ct);

        // 4. Push leaderboard snapshot to all clients in the competition group
        var payloads = entries.Select(e => new LeaderboardEntryPayload(
            e.Rank, e.TeamId, e.TeamName, e.TotalScore, e.SolvedCount));

        await hubNotifier.NotifyLeaderboardSnapshotAsync(solvedEvent.CompetitionId, payloads, ct);

        // 5. Notify the specific team's new score and rank
        var teamEntry = entries.FirstOrDefault(e => e.TeamId == solvedEvent.TeamId);
        if (teamEntry is not null)
        {
            await hubNotifier.NotifyScoreUpdateAsync(
                solvedEvent.CompetitionId,
                solvedEvent.TeamId,
                solvedEvent.TeamName,
                teamEntry.TotalScore,
                teamEntry.Rank,
                ct);
        }
    }
}
