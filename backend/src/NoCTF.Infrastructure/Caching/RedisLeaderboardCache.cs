using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Caching;

public sealed class RedisLeaderboardCache(NoCtfDbContext db, IConnectionMultiplexer? redis = null) : ILeaderboardCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct)
    {
        if (redis is null) return null;
        var payload = await redis.GetDatabase().StringGetAsync(Key(competitionId));
        return payload.IsNullOrEmpty ? null : JsonSerializer.Deserialize<LeaderboardResponse>(payload.ToString(), JsonOptions);
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (redis is null) return;
        var teams = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == competitionId && !x.Deletion.IsDeleted && !x.Ban.IsBanned)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var facts = await db.ScoringEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.Result == ScoringResult.Correct && x.TeamId != null)
            .Select(x => new { TeamId = x.TeamId!.Value, x.ChallengeId, x.OccurredAt }).ToListAsync(ct);
        var entries = teams.Select(team =>
        {
            var successful = facts.Where(x => x.TeamId == team.Id).OrderBy(x => x.OccurredAt).ToList();
            return new LeaderboardEntry(0, team.Id, team.Name, successful.Count, successful.Count,
                successful.LastOrDefault()?.OccurredAt,
                successful.Where(x => x.ChallengeId is not null).GroupBy(x => x.ChallengeId!.Value)
                    .Select(x => new LeaderboardChallengeSummary(x.Key, string.Empty, x.Count())).ToList());
        }).OrderByDescending(x => x.Score).ThenByDescending(x => x.SolveCount).ThenBy(x => x.LastScoreAt ?? DateTimeOffset.MaxValue)
          .ThenBy(x => x.TeamName, StringComparer.Ordinal).Select((x, index) => x with { Rank = index + 1 }).ToList();
        var response = new LeaderboardResponse(competitionId, DateTimeOffset.UtcNow, entries);
        await redis.GetDatabase().StringSetAsync(Key(competitionId), JsonSerializer.Serialize(response, JsonOptions), TimeSpan.FromMinutes(1));
    }

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct) => redis is null ? Task.CompletedTask : redis.GetDatabase().KeyDeleteAsync(Key(competitionId));
    private static string Key(Guid competitionId) => $"leaderboard:{competitionId:N}";
}
