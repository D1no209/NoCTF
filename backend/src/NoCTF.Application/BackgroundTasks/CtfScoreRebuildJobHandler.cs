using System.Text.Json;
using NoCTF.Core;
using NoCTF.Application.Scoring;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Events;

namespace NoCTF.Application.BackgroundTasks;

public sealed record CtfScoreRebuildPayload(Guid ChallengeId);

public sealed class CtfScoreRebuildJobHandler(
    ICtfScoreRebuilder rebuilder,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier) : ICompetitionJobHandler
{
    public const string JobType = "ctf.score.rebuild";
    public string JobKey => JobType;

    public async Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<CtfScoreRebuildPayload>(
                          task.PayloadJson,
                          new JsonSerializerOptions(JsonSerializerDefaults.Web))
                      ?? throw new InvalidOperationException("CTF score rebuild payload is invalid.");
        await rebuilder.RebuildChallengeAsync(task.CompetitionId, payload.ChallengeId, ct);

        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(task.CompetitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(task.CompetitionId, ct);
        await leaderboardCache.UpdateAsync(task.CompetitionId, entries, cacheVersion, ct);
        await hubNotifier.NotifyLeaderboardSnapshotAsync(
            task.CompetitionId,
            entries.Select(entry => new LeaderboardEntryPayload(
                entry.Rank,
                entry.TeamId,
                entry.TeamName,
                entry.TotalScore,
                entry.SolvedCount)),
            ct);
    }
}
