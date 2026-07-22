using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Notifications;

namespace NoCTF.Infrastructure.Caching;

public sealed class RedisLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    IConfiguration configuration,
    ILeaderboardRefreshPublisher publisher,
    IConnectionMultiplexer? redis = null) : ILeaderboardCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan ttl = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Leaderboard:CacheTtlSeconds", 60)));
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
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId)
            .Join(db.Challenges.AsNoTracking(), instance => instance.ChallengeId, template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .Select(item => new LeaderboardChallengeFact(
                item.Instance.Id,
                item.Template.Direction,
                item.Instance.Deletion.IsDeleted || item.Template.Deletion.IsDeleted,
                item.Instance.ConfigurationJson))
            .ToListAsync(ct);
        var competitionConfiguration = competition.ConfigurationJson;
        var submissions = await db.Submissions.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.ScoringEventId != null)
            .Join(db.ScoringEvents.AsNoTracking(), s => s.ScoringEventId, e => e.Id, (s, e) => new LeaderboardSubmissionFact(
                s.Id, s.TeamId!.Value, s.CompetitionChallengeId, s.Kind, s.ReceivedAt, e,
                s.SubjectTeamId, s.VictimTeamId, s.ServiceId, s.ControlIntervalSeconds, s.StageId)).ToListAsync(ct);
        var system = await db.ScoringEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.SubmissionId == null)
            .Select(x => new LeaderboardSystemFact(x)).ToListAsync(ct);
        var projection = projectionEngine.Project(
            new(competitionId, competition.Mode, teams, submissions, system, challenges, competitionConfiguration, competition.StartTime));
        var response = new LeaderboardResponse(competitionId, DateTimeOffset.UtcNow, projection.Entries)
        {
            Subjects = projection.Subjects,
            FirstBloods = projection.FirstBloods
        };
        await redis.GetDatabase().StringSetAsync(Key(competitionId), JsonSerializer.Serialize(response, JsonOptions), ttl);
        await publisher.PublishAsync(competitionId, response.GeneratedAt, ct);
    }

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct) => redis is null ? Task.CompletedTask : redis.GetDatabase().KeyDeleteAsync(Key(competitionId));
    private static string Key(Guid competitionId) => $"leaderboard:{competitionId:N}";
}
