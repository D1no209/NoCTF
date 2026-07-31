using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class RedisLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    IConfiguration configuration,
    ILeaderboardRefreshPublisher publisher,
    IConnectionMultiplexer? redis = null) : ILeaderboardCache
{
    private const string SnapshotField = "snapshot";
    private const string SnapshotRevisionField = "snapshotRevision";
    private const string DirtyRevisionField = "dirtyRevision";
    private const string FailureRevisionField = "failureRevision";
    private const string FailureAtField = "failureAt";

    private const string MarkDirtyScript = """
        local function compareRevision(left, right)
            if not left then return -1 end
            left = string.gsub(left, '^0+', '')
            right = string.gsub(right, '^0+', '')
            if left == '' then left = '0' end
            if right == '' then right = '0' end
            if string.len(left) < string.len(right) then return -1 end
            if string.len(left) > string.len(right) then return 1 end
            if left < right then return -1 end
            if left > right then return 1 end
            return 0
        end

        local current = redis.call('HGET', KEYS[1], 'dirtyRevision')
        local advances = compareRevision(current, ARGV[1]) < 0
        if advances then
            redis.call('HSET', KEYS[1], 'dirtyRevision', ARGV[1])
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return advances and 1 or 0
        """;

    private const string StoreSnapshotScript = """
        local function compareRevision(left, right)
            if not left then return -1 end
            left = string.gsub(left, '^0+', '')
            right = string.gsub(right, '^0+', '')
            if left == '' then left = '0' end
            if right == '' then right = '0' end
            if string.len(left) < string.len(right) then return -1 end
            if string.len(left) > string.len(right) then return 1 end
            if left < right then return -1 end
            if left > right then return 1 end
            return 0
        end

        local candidate = ARGV[1]
        local current = redis.call('HGET', KEYS[1], 'snapshotRevision')
        local comparison = compareRevision(current, candidate)
        local hasSnapshot = redis.call('HEXISTS', KEYS[1], 'snapshot') == 1
        local satisfied = candidate

        if comparison > 0 or (comparison == 0 and hasSnapshot) then
            satisfied = current
        else
            redis.call('HSET', KEYS[1],
                'snapshotRevision', ARGV[1],
                'snapshot', ARGV[2])
        end

        local dirty = redis.call('HGET', KEYS[1], 'dirtyRevision')
        if dirty and compareRevision(dirty, satisfied) <= 0 then
            redis.call('HDEL', KEYS[1], 'dirtyRevision')
        end

        local failure = redis.call('HGET', KEYS[1], 'failureRevision')
        if failure and compareRevision(failure, satisfied) <= 0 then
            redis.call('HDEL', KEYS[1], 'failureRevision', 'failureAt')
        end

        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        if comparison > 0 or (comparison == 0 and hasSnapshot) then
            return 0
        end
        return 1
        """;

    private const string SatisfyStateScript = """
        local function compareRevision(left, right)
            if not left then return -1 end
            left = string.gsub(left, '^0+', '')
            right = string.gsub(right, '^0+', '')
            if left == '' then left = '0' end
            if right == '' then right = '0' end
            if string.len(left) < string.len(right) then return -1 end
            if string.len(left) > string.len(right) then return 1 end
            if left < right then return -1 end
            if left > right then return 1 end
            return 0
        end

        local satisfied = ARGV[1]
        local dirty = redis.call('HGET', KEYS[1], 'dirtyRevision')
        if dirty and compareRevision(dirty, satisfied) <= 0 then
            redis.call('HDEL', KEYS[1], 'dirtyRevision')
        end
        local failure = redis.call('HGET', KEYS[1], 'failureRevision')
        if failure and compareRevision(failure, satisfied) <= 0 then
            redis.call('HDEL', KEYS[1], 'failureRevision', 'failureAt')
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return 1
        """;

    private const string RecordFailureScript = """
        local function compareRevision(left, right)
            if not left then return -1 end
            left = string.gsub(left, '^0+', '')
            right = string.gsub(right, '^0+', '')
            if left == '' then left = '0' end
            if right == '' then right = '0' end
            if string.len(left) < string.len(right) then return -1 end
            if string.len(left) > string.len(right) then return 1 end
            if left < right then return -1 end
            if left > right then return 1 end
            return 0
        end

        local failed = ARGV[1]
        local snapshot = redis.call('HGET', KEYS[1], 'snapshotRevision')
        if snapshot and compareRevision(snapshot, failed) >= 0 then
            return 0
        end
        local current = redis.call('HGET', KEYS[1], 'failureRevision')
        if current and compareRevision(current, failed) > 0 then
            return 0
        end
        redis.call('HSET', KEYS[1],
            'failureRevision', ARGV[1],
            'failureAt', ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        return 1
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan ttl = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Leaderboard:CacheTtlSeconds", 60)));

    public async Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct)
    {
        if (redis is null) return null;
        ct.ThrowIfCancellationRequested();
        var payload = await redis.GetDatabase().HashGetAsync(
            StateKey(competitionId),
            SnapshotField);
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
        long? attemptedRevision = null;
        LeaderboardResponse? response = null;
        var stored = false;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                ct);
            await CompetitionWriteLock.AcquireTransactionLockAsync(
                db,
                competitionId,
                ct);
            var competition = await db.Competitions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == competitionId, ct);
            if (competition is null)
            {
                await transaction.CommitAsync(ct);
                return;
            }

            attemptedRevision = competition.LeaderboardRevision;
            var database = redis.GetDatabase();
            var state = await database.HashGetAsync(
                StateKey(competitionId),
                [SnapshotRevisionField, SnapshotField]);
            if (TryParseRevision(state[0], out var currentRevision)
                && currentRevision >= attemptedRevision.Value
                && !state[1].IsNullOrEmpty)
            {
                await database.ScriptEvaluateAsync(
                    SatisfyStateScript,
                    [StateKey(competitionId)],
                    [currentRevision, TtlMilliseconds]);
                await transaction.CommitAsync(ct);
                return;
            }

            var projectedAt = DateTimeOffset.UtcNow;
            var teams = await db.Teams.AsNoTracking()
                .Where(x => x.CompetitionId == competitionId
                    && x.RegistrationStatus == TeamRegistrationStatus.Approved)
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
                    item.Instance.RulesJson))
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
            response = new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
            {
                Subjects = projection.Subjects,
                Bloods = projection.Bloods,
                SnapshotRevision = competition.LeaderboardRevision,
                TargetRevision = competition.LeaderboardRevision,
                Stale = false
            };
            stored = (long)await database.ScriptEvaluateAsync(
                StoreSnapshotScript,
                [StateKey(competitionId)],
                [
                    response.SnapshotRevision,
                    JsonSerializer.Serialize(response, JsonOptions),
                    TtlMilliseconds
                ]) == 1;
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (attemptedRevision is long failedRevision)
            {
                try
                {
                    await redis.GetDatabase().ScriptEvaluateAsync(
                        RecordFailureScript,
                        [StateKey(competitionId)],
                        [
                            failedRevision,
                            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                            TtlMilliseconds
                        ]);
                }
                catch (RedisException)
                {
                    // Preserve the original projection failure when Redis is also unavailable.
                }
            }
            throw;
        }

        if (stored && response is not null)
            await publisher.PublishAsync(competitionId, response.GeneratedAt, ct);
    }

    public async Task InvalidateAsync(Guid competitionId, CancellationToken ct)
    {
        var targetRevision = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => (long?)competition.LeaderboardRevision)
            .SingleOrDefaultAsync(ct);
        if (redis is null || targetRevision is null)
            return;
        ct.ThrowIfCancellationRequested();
        await redis.GetDatabase().ScriptEvaluateAsync(
            MarkDirtyScript,
            [StateKey(competitionId)],
            [targetRevision.Value, TtlMilliseconds]);
    }

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
        ct.ThrowIfCancellationRequested();
        var failure = await redis.GetDatabase().HashGetAsync(
            StateKey(competitionId),
            [FailureRevisionField, FailureAtField]);
        var failureRevision = TryParseRevision(failure[0], out var parsedFailureRevision)
            ? parsedFailureRevision
            : -1;
        return new(
            targetRevision,
            failureRevision >= targetRevision
                && DateTimeOffset.TryParse(
                    failure[1].ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var failedAt)
                ? failedAt
                : null);
    }

    private long TtlMilliseconds => checked((long)ttl.TotalMilliseconds);

    private static bool TryParseRevision(RedisValue value, out long revision) =>
        long.TryParse(
            value.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out revision);

    private static RedisKey StateKey(Guid competitionId) =>
        $"leaderboard:v2:{{{competitionId:N}}}:state";
}
