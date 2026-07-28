using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

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
        if (payload.IsNullOrEmpty)
            return null;
        var snapshot = JsonSerializer.Deserialize<LeaderboardResponse>(payload.ToString(), JsonOptions);
        if (snapshot is null)
            return null;
        var status = await GetStatusAsync(competitionId, ct);
        return snapshot with
        {
            TargetRevision = status.TargetRevision,
            Stale = snapshot.SnapshotRevision < status.TargetRevision,
            LastFailureAt = status.LastFailureAt
        };
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (redis is null) return;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        if (competition is null) return;
        var projectedAt = DateTimeOffset.UtcNow;
        try
        {
        var teams = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Select(x => new LeaderboardTeamFact(
                x.Id, x.Name, x.IsBanned, x.DeletedAt != null, x.RegisteredAt))
            .ToListAsync(ct);
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId)
            .Join(db.Challenges.AsNoTracking(), instance => instance.ChallengeId, template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .Select(item => new LeaderboardChallengeFact(
                item.Instance.Id,
                item.Template.Direction,
                item.Instance.DeletedAt != null || item.Template.DeletedAt != null,
                item.Instance.ConfigurationJson))
            .ToListAsync(ct);
        var competitionConfiguration = competition.ConfigurationJson;
        var submissions = await db.Submissions.AsNoTracking()
            .Where(x => x.CompetitionId == competitionId && x.CurrentScoringEventId != null)
            .Join(db.ScoringEvents.AsNoTracking(), s => s.CurrentScoringEventId, e => (Guid?)e.Id,
                (s, e) => new LeaderboardSubmissionFact(
                    s.Id, s.TeamId, s.CompetitionChallengeId, s.Kind, s.ReceivedAt, e,
                    e.VictimTeamId))
            .ToListAsync(ct);
        var system = await db.ScoringEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.SubmissionId == null)
            .Select(x => new LeaderboardSystemFact(
                x,
                x.Kind == ScoringEventKind.HintUnlock && x.SpecificationId != null
                    ? db.Set<CompetitionChallengeHint>()
                        .Where(hint => hint.Id == x.SpecificationId && hint.DeletedAt == null)
                        .Select(hint => hint.Cost)
                        .SingleOrDefault()
                    : 0))
            .ToListAsync(ct);
        var lifecycleAudits = await db.Set<CompetitionLifecycleAudit>().AsNoTracking()
            .Where(audit => audit.CompetitionId == competitionId)
            .ToListAsync(ct);
        IReadOnlyList<LeaderboardAwdRoundFact> awdRounds = [];
        if (competition.Mode == GameMode.Awd)
        {
            awdRounds = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.TeamId != null
                    && flag.CompetitionChallengeId != null
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId != null
                    && flag.ValidStart != null
                    && flag.ValidUntil != null
                    && flag.DeletedAt == null)
                .Join(
                    db.CompetitionChallenges.AsNoTracking()
                        .Where(challenge => challenge.CompetitionId == competitionId),
                    flag => flag.CompetitionChallengeId,
                    challenge => (Guid?)challenge.Id,
                    (flag, _) => new LeaderboardAwdRoundFact(
                        flag.CompetitionChallengeId!.Value,
                        flag.TeamId!.Value,
                        flag.SpecificationId!.Value,
                        flag.ValidStart!.Value,
                        flag.ValidUntil!.Value))
                .ToListAsync(ct);
        }
        var projection = projectionEngine.Project(
            new(
                competitionId,
                competition.Mode,
                teams,
                submissions,
                system,
                challenges,
                competitionConfiguration,
                competition.StartAt,
                lifecycleAudits,
                awdRounds,
                projectedAt));
        var response = new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
        {
            Subjects = projection.Subjects,
            FirstBloods = projection.FirstBloods,
            SnapshotRevision = competition.LeaderboardRevision,
            TargetRevision = competition.LeaderboardRevision,
            Stale = false
        };
        var database = redis.GetDatabase();
        await database.StringSetAsync(Key(competitionId), JsonSerializer.Serialize(response, JsonOptions), ttl);
        await database.KeyDeleteAsync(FailureKey(competitionId));
        await publisher.PublishAsync(competitionId, response.GeneratedAt, ct);
        }
        catch
        {
            await redis.GetDatabase().StringSetAsync(
                FailureKey(competitionId),
                DateTimeOffset.UtcNow.ToString("O"),
                ttl);
            throw;
        }
    }

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct) => Task.CompletedTask;

    public async Task<LeaderboardCacheStatus> GetStatusAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var targetRevision = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.LeaderboardRevision)
            .SingleOrDefaultAsync(ct);
        if (redis is null)
            return new(targetRevision, null);
        var failure = await redis.GetDatabase().StringGetAsync(FailureKey(competitionId));
        return new(
            targetRevision,
            DateTimeOffset.TryParse(failure.ToString(), out var failedAt)
                ? failedAt
                : null);
    }
    private static string Key(Guid competitionId) => $"leaderboard:{competitionId:N}";
    private static string FailureKey(Guid competitionId) => $"leaderboard:{competitionId:N}:failure";
}
