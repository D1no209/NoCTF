using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Caching;

public sealed class RedisLeaderboardCache(NoCtfDbContext db, ILeaderboardProjectorCatalog projectors, IConnectionMultiplexer? redis = null) : ILeaderboardCache
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
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        if (competition is null) return;
        var teams = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Select(x => new LeaderboardTeamFact(x.Id, x.Name, x.Ban.IsBanned, x.Deletion.IsDeleted)).ToListAsync(ct);
        var challenges = await db.Challenges.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Select(x => new LeaderboardChallengeFact(x.Id, x.Direction, x.Deletion.IsDeleted)).ToListAsync(ct);
        var submissions = await db.Submissions.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.ScoringEventId != null)
            .Join(db.ScoringEvents.AsNoTracking(), s => s.ScoringEventId, e => e.Id, (s, e) => new LeaderboardSubmissionFact(s.Id, s.TeamId!.Value, s.ChallengeId, s.Kind, s.ReceivedAt, e)).ToListAsync(ct);
        var system = await db.ScoringEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.SubmissionId == null)
            .Select(x => new LeaderboardSystemFact(x)).ToListAsync(ct);
        var entries = projectors.Get(competition.Mode).Project(
            new(competitionId, competition.Mode, teams, submissions, system, challenges));
        var response = new LeaderboardResponse(competitionId, DateTimeOffset.UtcNow, entries);
        await redis.GetDatabase().StringSetAsync(Key(competitionId), JsonSerializer.Serialize(response, JsonOptions), TimeSpan.FromMinutes(1));
    }

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct) => redis is null ? Task.CompletedTask : redis.GetDatabase().KeyDeleteAsync(Key(competitionId));
    private static string Key(Guid competitionId) => $"leaderboard:{competitionId:N}";
}
