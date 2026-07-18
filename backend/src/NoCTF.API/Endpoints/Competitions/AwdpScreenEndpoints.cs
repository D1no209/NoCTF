using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;

namespace NoCTF.API.Endpoints.Competitions;

public class AwdpScreenRequest
{
    public Guid GameId { get; set; }
}

public class AwdpScreenSnapshotDto
{
    public AwdpScreenGameDto Game { get; set; } = new();
    public AwdpScreenStatsDto Stats { get; set; } = new();
    public List<AwdpScreenTeamScoreDto> Scoreboard { get; set; } = [];
    public List<AwdpScreenChallengeStatusDto> Challenges { get; set; } = [];
    public List<AwdpScreenTeamChallengeStateDto> TeamChallengeStates { get; set; } = [];
    public List<AwdpScreenEventDto> RecentEvents { get; set; } = [];
    public List<AwdpScreenRoundStatDto> RoundTimeline { get; set; } = [];
}

public class AwdpScreenGameDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public string Phase { get; set; } = "waiting";
    public DateTime ServerTime { get; set; }
    public DateTime? RoundStartedAt { get; set; }
    public DateTime? RoundEndsAt { get; set; }
}

public class AwdpScreenStatsDto
{
    public int TeamCount { get; set; }
    public int ChallengeCount { get; set; }
    public int TotalAttackCount { get; set; }
    public int TotalDefenseCount { get; set; }
    public int AttackSuccessCount { get; set; }
    public int AttackFailCount { get; set; }
    public int DefenseSuccessCount { get; set; }
    public int DefenseFailCount { get; set; }
    public int ActiveTeamCount { get; set; }
    public int ActiveChallengeCount { get; set; }
    public int TotalScoreDelta { get; set; }
}

public class AwdpScreenTeamScoreDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int? PreviousRank { get; set; }
    public int TotalScore { get; set; }
    public int AttackScore { get; set; }
    public int DefenseScore { get; set; }
    public int CurrentRoundScore { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public string Trend { get; set; } = "stable";
}

public class AwdpScreenChallengeStatusDto
{
    public Guid ChallengeId { get; set; }
    public string ChallengeName { get; set; } = string.Empty;
    public string Category { get; set; } = "misc";
    public int AttackHeat { get; set; }
    public int DefensePassedCount { get; set; }
    public int DefenseFailedCount { get; set; }
    public int InstanceCount { get; set; }
    public int ActiveTeamCount { get; set; }
    public DateTime? LastEventAt { get; set; }
}

public class AwdpScreenTeamChallengeStateDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public Guid ChallengeId { get; set; }
    public string ChallengeName { get; set; } = string.Empty;
    public string InstanceStatus { get; set; } = string.Empty;
    public string BreakStatus { get; set; } = string.Empty;
    public string FixStatus { get; set; } = string.Empty;
    public string ServiceStatus { get; set; } = string.Empty;
    public int AttackAttempts { get; set; }
    public int DefenseAttempts { get; set; }
    public int CurrentRoundScore { get; set; }
    public DateTime? LastActivityAt { get; set; }
}

public class AwdpScreenEventDto
{
    public string Id { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    public int Round { get; set; }
    public string Type { get; set; } = "SCORE_UPDATED";
    public Guid? TeamId { get; set; }
    public string? TeamName { get; set; }
    public Guid? ChallengeId { get; set; }
    public string? ChallengeName { get; set; }
    public string? ChallengeCategory { get; set; }
    public int? ScoreDelta { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AwdpScreenRoundStatDto
{
    public int Round { get; set; }
    public int AttackSuccessCount { get; set; }
    public int DefenseSuccessCount { get; set; }
    public int AttackFailCount { get; set; }
    public int DefenseFailCount { get; set; }
    public int ScoreDelta { get; set; }
    public int ActiveTeamCount { get; set; }
}

public class AwdpScreenSnapshotEndpoint(AwdpScreenSnapshotCache snapshotCache)
    : Endpoint<AwdpScreenRequest, AwdpScreenSnapshotDto>
{
    public override void Configure()
    {
        Get("/api/awdp/screen/snapshot");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(AwdpScreenRequest req, CancellationToken ct)
    {
        var snapshot = await snapshotCache.GetAsync(req.GameId, ct);
        if (snapshot is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(snapshot, cancellation: ct);
    }
}

public class AwdpScreenEventsEndpoint(AwdpScreenSnapshotCache snapshotCache)
    : Endpoint<AwdpScreenRequest>
{
    public override void Configure()
    {
        Get("/api/awdp/screen/events");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-stream"));
    }

    public override async Task HandleAsync(AwdpScreenRequest req, CancellationToken ct)
    {
        var firstSnapshot = await snapshotCache.GetCachedAsync(req.GameId, ct);
        if (firstSnapshot.Snapshot is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        HttpContext.Response.ContentType = "text/event-stream; charset=utf-8";
        HttpContext.Response.Headers.CacheControl = "no-cache, no-transform";
        HttpContext.Response.Headers.Connection = "keep-alive";
        HttpContext.Response.Headers["X-Accel-Buffering"] = "no";

        await WriteSnapshotAsync(firstSnapshot.SsePayload, ct);
        var currentVersion = firstSnapshot.Version;
        var nextHeartbeatAt = DateTime.UtcNow.Add(AwdpScreenSnapshotCache.HeartbeatInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                var snapshot = await snapshotCache.GetCachedAsync(req.GameId, ct);
                if (snapshot.Snapshot is null)
                    return;

                if (!string.Equals(snapshot.Version, currentVersion, StringComparison.Ordinal))
                {
                    await WriteSnapshotAsync(snapshot.SsePayload, ct);
                    currentVersion = snapshot.Version;
                    nextHeartbeatAt = DateTime.UtcNow.Add(AwdpScreenSnapshotCache.HeartbeatInterval);
                }
                else if (DateTime.UtcNow >= nextHeartbeatAt)
                {
                    await HttpContext.Response.Body.WriteAsync(AwdpScreenSnapshotCache.HeartbeatPayload, ct);
                    await HttpContext.Response.Body.FlushAsync(ct);
                    nextHeartbeatAt = DateTime.UtcNow.Add(AwdpScreenSnapshotCache.HeartbeatInterval);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task WriteSnapshotAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await HttpContext.Response.Body.WriteAsync(payload, ct);
        await HttpContext.Response.Body.FlushAsync(ct);
    }
}

internal sealed record AwdpScreenCachedSnapshot(
    AwdpScreenSnapshotDto? Snapshot,
    string? Version,
    ReadOnlyMemory<byte> SsePayload);

public sealed class AwdpScreenSnapshotCache
{
    internal const int DefaultCapacity = 128;
    internal const int DefaultStripeCount = 32;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan EntryRetention = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    internal static readonly ReadOnlyMemory<byte> HeartbeatPayload = Encoding.UTF8.GetBytes(": heartbeat\n\n");
    private static readonly byte[] SsePrefix = Encoding.UTF8.GetBytes("data: ");
    private static readonly byte[] SseSuffix = Encoding.UTF8.GetBytes("\n\n");
    private static readonly JsonSerializerOptions VersionJsonOptions = CreateVersionJsonOptions();

    private readonly Func<Guid, CancellationToken, Task<AwdpScreenSnapshotDto?>> _snapshotLoader;
    private readonly int _capacity;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _entryRetention;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly SemaphoreSlim[] _stripes;
    private readonly object _cacheMutationLock = new();

    public AwdpScreenSnapshotCache(IServiceScopeFactory scopeFactory)
        : this(
            async (competitionId, ct) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await AwdpScreenSnapshotBuilder.BuildAsync(db, competitionId, ct);
            },
            DefaultCapacity,
            DefaultStripeCount,
            CacheDuration,
            EntryRetention)
    {
    }

    internal AwdpScreenSnapshotCache(
        Func<Guid, CancellationToken, Task<AwdpScreenSnapshotDto?>> snapshotLoader,
        int capacity = DefaultCapacity,
        int stripeCount = DefaultStripeCount,
        TimeSpan? cacheDuration = null,
        TimeSpan? entryRetention = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotLoader);
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (stripeCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(stripeCount));

        _snapshotLoader = snapshotLoader;
        _capacity = capacity;
        _cacheDuration = cacheDuration ?? CacheDuration;
        _entryRetention = entryRetention ?? EntryRetention;
        _stripes = Enumerable.Range(0, stripeCount)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();
    }

    internal int CachedEntryCount => _entries.Count;
    internal int StripeCount => _stripes.Length;

    public async Task<AwdpScreenSnapshotDto?> GetAsync(Guid competitionId, CancellationToken ct)
        => (await GetCachedAsync(competitionId, ct)).Snapshot;

    internal async Task<AwdpScreenCachedSnapshot> GetCachedAsync(Guid competitionId, CancellationToken ct)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (TryGetFresh(competitionId, nowTicks, out var cached))
            return cached;

        var stripe = _stripes[(int)((uint)competitionId.GetHashCode() % (uint)_stripes.Length)];
        await stripe.WaitAsync(ct);
        try
        {
            nowTicks = DateTime.UtcNow.Ticks;
            if (TryGetFresh(competitionId, nowTicks, out cached))
                return cached;

            var snapshot = await _snapshotLoader(competitionId, ct);
            var value = CreateCachedSnapshot(snapshot);
            nowTicks = DateTime.UtcNow.Ticks;
            Store(competitionId, new CacheEntry(
                value,
                nowTicks + _cacheDuration.Ticks,
                nowTicks + _entryRetention.Ticks,
                nowTicks), nowTicks);
            return value;
        }
        finally
        {
            stripe.Release();
        }
    }

    internal static AwdpScreenCachedSnapshot CreateCachedSnapshot(AwdpScreenSnapshotDto? snapshot)
    {
        if (snapshot is null)
            return new AwdpScreenCachedSnapshot(null, null, ReadOnlyMemory<byte>.Empty);

        var versionBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, VersionJsonOptions);
        var version = Convert.ToHexString(SHA256.HashData(versionBytes));
        var json = JsonSerializer.SerializeToUtf8Bytes(new { snapshot }, AwdpScreenSnapshotBuilder.JsonOptions);
        var payload = new byte[SsePrefix.Length + json.Length + SseSuffix.Length];
        SsePrefix.CopyTo(payload, 0);
        json.CopyTo(payload, SsePrefix.Length);
        SseSuffix.CopyTo(payload, SsePrefix.Length + json.Length);
        return new AwdpScreenCachedSnapshot(snapshot, version, payload);
    }

    private bool TryGetFresh(Guid competitionId, long nowTicks, out AwdpScreenCachedSnapshot value)
    {
        if (_entries.TryGetValue(competitionId, out var entry) && entry.FreshUntilTicks > nowTicks)
        {
            Volatile.Write(ref entry.LastAccessTicks, nowTicks);
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private void Store(Guid competitionId, CacheEntry entry, long nowTicks)
    {
        lock (_cacheMutationLock)
        {
            foreach (var candidate in _entries)
            {
                if (candidate.Value.RetainUntilTicks <= nowTicks)
                    _entries.TryRemove(candidate.Key, out _);
            }

            if (!_entries.ContainsKey(competitionId) && _entries.Count >= _capacity)
            {
                var victim = _entries
                    .OrderBy(candidate => Volatile.Read(ref candidate.Value.LastAccessTicks))
                    .FirstOrDefault();
                if (!victim.Equals(default(KeyValuePair<Guid, CacheEntry>)))
                    _entries.TryRemove(victim.Key, out _);
            }

            _entries[competitionId] = entry;
        }
    }

    private static JsonSerializerOptions CreateVersionJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(AwdpScreenGameDto))
                return;

            var serverTime = typeInfo.Properties.FirstOrDefault(property =>
                property.Name.Equals("serverTime", StringComparison.OrdinalIgnoreCase));
            if (serverTime is not null)
                serverTime.ShouldSerialize = static (_, _) => false;
        });
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver
        };
    }

    private sealed class CacheEntry(
        AwdpScreenCachedSnapshot value,
        long freshUntilTicks,
        long retainUntilTicks,
        long lastAccessTicks)
    {
        public AwdpScreenCachedSnapshot Value { get; } = value;
        public long FreshUntilTicks { get; } = freshUntilTicks;
        public long RetainUntilTicks { get; } = retainUntilTicks;
        public long LastAccessTicks = lastAccessTicks;
    }
}

internal static class AwdpScreenSnapshotBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    public static async Task<AwdpScreenSnapshotDto?> BuildAsync(
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == competitionId &&
                c.GameModeType == GameModeType.Awdp &&
                (c.Status == CompetitionStatus.Published ||
                 c.Status == CompetitionStatus.Running ||
                 c.Status == CompetitionStatus.Paused ||
                 c.Status == CompetitionStatus.Finished), ct);

        if (competition is null)
            return null;

        if (competition.Status != CompetitionStatus.Finished && now < competition.StartTime)
            return null;

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        var activeTeamIds = teams.Select(t => t.Id).ToList();
        var activeChallengeIds = challenges.Select(c => c.Id).ToList();

        var states = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                activeTeamIds.Contains(s.TeamId) &&
                activeChallengeIds.Contains(s.ChallengeId))
            .ToListAsync(ct);

        var currentRound = await db.AwdpRounds
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.CompetitionId == competitionId)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(ct);
        var currentRoundNumber = currentRound?.RoundNumber ?? 0;

        var roundScoreQuery = db.AwdpRoundScores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                activeTeamIds.Contains(s.TeamId) &&
                activeChallengeIds.Contains(s.ChallengeId));
        var currentRoundScores = currentRoundNumber == 0
            ? []
            : await roundScoreQuery
                .Where(s => s.RoundNumber == currentRoundNumber)
                .ToListAsync(ct);
        var scoreTotalsByTeam = await roundScoreQuery
            .GroupBy(s => s.TeamId)
            .Select(g => new
            {
                TeamId = g.Key,
                AttackScore = g.Sum(s => s.AttackScoreDelta),
                DefenseScore = g.Sum(s => s.DefenseScoreDelta)
            })
            .ToDictionaryAsync(row => row.TeamId, ct);
        var timeline = await roundScoreQuery
            .GroupBy(s => s.RoundNumber)
            .Select(g => new AwdpScreenRoundStatDto
            {
                Round = g.Key,
                AttackSuccessCount = g.Count(s => s.AttackScoreDelta > 0),
                DefenseSuccessCount = g.Count(s => s.DefenseScoreDelta > 0),
                AttackFailCount = 0,
                DefenseFailCount = g.Count(s => s.PenaltyDelta > 0),
                ScoreDelta = g.Sum(s => s.RoundScoreDelta),
                ActiveTeamCount = g.Select(s => s.TeamId).Distinct().Count()
            })
            .OrderByDescending(row => row.Round)
            .Take(240)
            .ToListAsync(ct);
        timeline.Reverse();

        var scoreEvents = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == competitionId &&
                activeTeamIds.Contains(e.TeamId) &&
                (!e.ChallengeId.HasValue || activeChallengeIds.Contains(e.ChallengeId.Value)))
            .OrderByDescending(e => e.Timestamp)
            .Take(80)
            .ToListAsync(ct);

        var patchSubmissions = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                activeTeamIds.Contains(s.TeamId) &&
                activeChallengeIds.Contains(s.ChallengeId))
            .OrderByDescending(s => s.SubmittedAt)
            .Take(60)
            .ToListAsync(ct);

        var competitionLogs = await db.CompetitionLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l =>
                l.CompetitionId == competitionId &&
                l.EventType.StartsWith("awdp.") &&
                (!l.TeamId.HasValue || activeTeamIds.Contains(l.TeamId.Value)) &&
                (!l.ChallengeId.HasValue || activeChallengeIds.Contains(l.ChallengeId.Value)))
            .OrderByDescending(l => l.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        var teamNames = teams.ToDictionary(t => t.Id, t => t.Name);
        var challengeNames = challenges.ToDictionary(c => c.Id, c => c.Title);
        var challengeCategories = challenges.ToDictionary(c => c.Id, c => NormalizeCategory(c.Direction));

        var roundScoreByTeam = currentRoundScores
            .GroupBy(s => s.TeamId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.RoundScoreDelta));

        var scoreByTeam = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == competitionId &&
                activeTeamIds.Contains(e.TeamId) &&
                (!e.ChallengeId.HasValue || activeChallengeIds.Contains(e.ChallengeId.Value)))
            .GroupBy(e => e.TeamId)
            .Select(g => new { TeamId = g.Key, Score = g.Sum(e => e.PointsDelta) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Score, ct);

        var lastActiveByTeam = states
            .GroupBy(s => s.TeamId)
            .ToDictionary(g => g.Key, g => g.Max(LastActivityAt));

        var scoreboard = teams
            .Select(t => new AwdpScreenTeamScoreDto
            {
                TeamId = t.Id,
                TeamName = t.Name,
                TotalScore = scoreByTeam.GetValueOrDefault(t.Id),
                AttackScore = scoreTotalsByTeam.GetValueOrDefault(t.Id)?.AttackScore ?? 0,
                DefenseScore = scoreTotalsByTeam.GetValueOrDefault(t.Id)?.DefenseScore ?? 0,
                CurrentRoundScore = roundScoreByTeam.GetValueOrDefault(t.Id),
                LastActiveAt = lastActiveByTeam.GetValueOrDefault(t.Id),
                Trend = roundScoreByTeam.GetValueOrDefault(t.Id) > 0 ? "up" : "stable"
            })
            .OrderByDescending(t => t.TotalScore)
            .ThenBy(t => t.TeamName)
            .ToList();

        for (var i = 0; i < scoreboard.Count; i++)
            scoreboard[i].Rank = i + 1;

        var rankByTeam = scoreboard.ToDictionary(t => t.TeamId, t => t.Rank);
        var stateByTeamChallenge = states.ToDictionary(s => (s.TeamId, s.ChallengeId));
        var statesByChallenge = states.ToLookup(s => s.ChallengeId);
        var roundScoreByTeamChallenge = currentRoundScores
            .GroupBy(s => (s.TeamId, s.ChallengeId))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.RoundScoreDelta));

        var challengeStatuses = challenges.Select(challenge =>
        {
            var challengeStates = statesByChallenge[challenge.Id];
            var lastEventAt = !challengeStates.Any()
                ? null
                : challengeStates.Max(LastActivityAt);
            return new AwdpScreenChallengeStatusDto
            {
                ChallengeId = challenge.Id,
                ChallengeName = challenge.Title,
                Category = NormalizeCategory(challenge.Direction),
                AttackHeat = Math.Min(100, challengeStates.Sum(s => s.AttackAttempts) * 10),
                DefensePassedCount = challengeStates.Count(s => s.FixStatus == AwdpFixStatus.FixSuccess),
                DefenseFailedCount = challengeStates.Count(s => IsDefenseFailure(s.FixStatus)),
                InstanceCount = challengeStates.Count(s => s.InstanceStatus == AwdpInstanceStatus.InstanceRunning),
                ActiveTeamCount = challengeStates.Count(s => LastActivityAt(s) is { } at && now - at <= ActiveWindow),
                LastEventAt = lastEventAt
            };
        }).ToList();

        var cells = new List<AwdpScreenTeamChallengeStateDto>();
        foreach (var team in teams)
        {
            foreach (var challenge in challenges)
            {
                stateByTeamChallenge.TryGetValue((team.Id, challenge.Id), out var state);
                cells.Add(new AwdpScreenTeamChallengeStateDto
                {
                    TeamId = team.Id,
                    TeamName = team.Name,
                    Rank = rankByTeam.GetValueOrDefault(team.Id),
                    ChallengeId = challenge.Id,
                    ChallengeName = challenge.Title,
                    InstanceStatus = (state?.InstanceStatus ?? AwdpInstanceStatus.InstanceNotCreated).ToString(),
                    BreakStatus = (state?.BreakStatus ?? AwdpBreakStatus.BreakNotStarted).ToString(),
                    FixStatus = (state?.FixStatus ?? AwdpFixStatus.FixNotStarted).ToString(),
                    ServiceStatus = (state?.ServiceStatus ?? AwdpServiceStatus.ServiceUnknown).ToString(),
                    AttackAttempts = state?.AttackAttempts ?? 0,
                    DefenseAttempts = state?.DefenseAttempts ?? 0,
                    CurrentRoundScore = roundScoreByTeamChallenge.GetValueOrDefault((team.Id, challenge.Id)),
                    LastActivityAt = state is null ? null : LastActivityAt(state)
                });
            }
        }

        var recentEvents = BuildEvents(competitionId, currentRoundNumber, scoreEvents, patchSubmissions, competitionLogs, states, teamNames, challengeNames, challengeCategories)
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .ToList();

        var activeCutoff = now - ActiveWindow;
        return new AwdpScreenSnapshotDto
        {
            Game = new AwdpScreenGameDto
            {
                Id = competition.Id,
                Title = competition.Title,
                Status = MapCompetitionStatus(competition.Status),
                CurrentRound = currentRoundNumber,
                TotalRounds = competition.TotalRounds ?? currentRoundNumber,
                Phase = MapPhase(competition.Status, currentRound?.Status),
                ServerTime = now,
                RoundStartedAt = currentRound?.StartTime,
                RoundEndsAt = ResolveRoundEnd(competition, currentRound)
            },
            Stats = new AwdpScreenStatsDto
            {
                TeamCount = teams.Count,
                ChallengeCount = challenges.Count,
                TotalAttackCount = states.Sum(s => s.AttackAttempts),
                TotalDefenseCount = states.Sum(s => s.DefenseAttempts),
                AttackSuccessCount = states.Count(s => s.BreakStatus == AwdpBreakStatus.BreakSuccess),
                AttackFailCount = states.Count(s => s.BreakStatus is AwdpBreakStatus.BreakFailed or AwdpBreakStatus.AttackAttemptsExhausted),
                DefenseSuccessCount = states.Count(s => s.FixStatus == AwdpFixStatus.FixSuccess),
                DefenseFailCount = states.Count(s => IsDefenseFailure(s.FixStatus)),
                ActiveTeamCount = scoreboard.Count(t => t.LastActiveAt >= activeCutoff),
                ActiveChallengeCount = challengeStatuses.Count(c => c.LastEventAt >= activeCutoff),
                TotalScoreDelta = timeline.Sum(r => r.ScoreDelta)
            },
            Scoreboard = scoreboard,
            Challenges = challengeStatuses,
            TeamChallengeStates = cells,
            RecentEvents = recentEvents,
            RoundTimeline = timeline
        };
    }

    private static IEnumerable<AwdpScreenEventDto> BuildEvents(
        Guid competitionId,
        int currentRoundNumber,
        IEnumerable<ScoreEvent> scoreEvents,
        IEnumerable<AwdpPatchSubmission> patchSubmissions,
        IEnumerable<CompetitionLog> competitionLogs,
        IEnumerable<AwdpTeamChallengeState> states,
        IReadOnlyDictionary<Guid, string> teamNames,
        IReadOnlyDictionary<Guid, string> challengeNames,
        IReadOnlyDictionary<Guid, string> challengeCategories)
    {
        foreach (var state in states.Where(s => s.ServiceStatus == AwdpServiceStatus.ServiceError))
        {
            yield return new AwdpScreenEventDto
            {
                Id = $"service-{state.Id:N}",
                GameId = competitionId,
                Round = currentRoundNumber,
                Type = "SERVICE_ERROR",
                TeamId = state.TeamId,
                TeamName = teamNames.GetValueOrDefault(state.TeamId),
                ChallengeId = state.ChallengeId,
                ChallengeName = challengeNames.GetValueOrDefault(state.ChallengeId),
                ChallengeCategory = challengeCategories.GetValueOrDefault(state.ChallengeId),
                Level = "danger",
                Message = "Challenge service is temporarily unavailable.",
                CreatedAt = LastActivityAt(state) ?? (state.UpdatedAt == default ? DateTime.UtcNow : state.UpdatedAt)
            };
        }

        foreach (var log in competitionLogs)
        {
            yield return new AwdpScreenEventDto
            {
                Id = $"log-{log.Id:N}",
                GameId = competitionId,
                Round = 0,
                Type = MapLogEventType(log.EventType),
                TeamId = log.TeamId,
                TeamName = log.TeamId is { } teamId ? teamNames.GetValueOrDefault(teamId) : null,
                ChallengeId = log.ChallengeId,
                ChallengeName = log.ChallengeId is { } challengeId ? challengeNames.GetValueOrDefault(challengeId) : null,
                ChallengeCategory = log.ChallengeId is { } categoryId ? challengeCategories.GetValueOrDefault(categoryId) : null,
                Level = MapEventLevel(log.Level, log.EventType),
                Message = string.Equals(log.Level, "error", StringComparison.OrdinalIgnoreCase)
                    ? "A competition service error occurred."
                    : log.Message,
                CreatedAt = log.CreatedAt
            };
        }

        foreach (var score in scoreEvents)
        {
            yield return new AwdpScreenEventDto
            {
                Id = $"score-{score.Id:N}",
                GameId = competitionId,
                Round = score.RoundNumber ?? 0,
                Type = "SCORE_UPDATED",
                TeamId = score.TeamId,
                TeamName = teamNames.GetValueOrDefault(score.TeamId),
                ChallengeId = score.ChallengeId,
                ChallengeName = score.ChallengeId is { } challengeId ? challengeNames.GetValueOrDefault(challengeId) : null,
                ChallengeCategory = score.ChallengeId is { } categoryId ? challengeCategories.GetValueOrDefault(categoryId) : null,
                ScoreDelta = score.PointsDelta,
                Level = score.PointsDelta < 0 ? "warning" : "success",
                CreatedAt = score.Timestamp
            };
        }

        foreach (var patch in patchSubmissions)
        {
            var isServiceError = patch.FixStatus == AwdpFixStatus.FixServiceError;
            yield return new AwdpScreenEventDto
            {
                Id = $"patch-{patch.Id:N}",
                GameId = competitionId,
                Round = 0,
                Type = isServiceError
                    ? "SERVICE_ERROR"
                    : patch.FixStatus == AwdpFixStatus.FixSuccess
                    ? "DEFENSE_CHECK_PASSED"
                    : IsDefenseFailure(patch.FixStatus) ? "DEFENSE_CHECK_FAILED" : "PATCH_UPLOADED",
                TeamId = patch.TeamId,
                TeamName = teamNames.GetValueOrDefault(patch.TeamId),
                ChallengeId = patch.ChallengeId,
                ChallengeName = challengeNames.GetValueOrDefault(patch.ChallengeId),
                ChallengeCategory = challengeCategories.GetValueOrDefault(patch.ChallengeId),
                Level = isServiceError ? "danger" : patch.FixStatus == AwdpFixStatus.FixSuccess ? "success" : IsDefenseFailure(patch.FixStatus) ? "warning" : "info",
                CreatedAt = patch.ValidatedAt ?? patch.SubmittedAt
            };
        }
    }

    private static string MapLogEventType(string eventType)
        => eventType switch
        {
            "awdp.break.success" => "ATTACK_ACCEPTED",
            "awdp.break.failed" => "ATTACK_REJECTED",
            "awdp.patch.submitted" => "PATCH_UPLOADED",
            "awdp.patch.verified" => "DEFENSE_CHECK_PASSED",
            "awdp.patch.rejected" => "DEFENSE_CHECK_FAILED",
            "awdp.service.error" => "SERVICE_ERROR",
            "awdp.service.failed" => "SERVICE_ERROR",
            _ => "SCORE_UPDATED"
        };

    private static string MapEventLevel(string level, string eventType)
    {
        if (eventType.EndsWith(".success", StringComparison.OrdinalIgnoreCase) ||
            eventType.EndsWith(".verified", StringComparison.OrdinalIgnoreCase))
            return "success";
        if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
            return "danger";
        if (string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase) ||
            eventType.EndsWith(".failed", StringComparison.OrdinalIgnoreCase) ||
            eventType.EndsWith(".rejected", StringComparison.OrdinalIgnoreCase))
            return "warning";
        return "info";
    }

    private static DateTime? LastActivityAt(AwdpTeamChallengeState state)
    {
        return new[]
            {
                state.LastBreakSubmittedAt,
                state.DefenseRequestedAt,
                state.LastFixSubmittedAt,
                state.BreakSucceededAt,
                state.FixSucceededAt,
                state.UpdatedAt == default ? null : state.UpdatedAt
            }
            .Where(value => value.HasValue)
            .Max();
    }

    private static bool IsDefenseFailure(AwdpFixStatus status)
        => status is AwdpFixStatus.FixFailed
            or AwdpFixStatus.FixServiceError
            or AwdpFixStatus.FixScriptError
            or AwdpFixStatus.FixTimeout
            or AwdpFixStatus.AuditFailed
            or AwdpFixStatus.FixRuleViolation
            or AwdpFixStatus.DefenseAttemptsExhausted;

    private static string NormalizeCategory(string? direction)
    {
        return direction?.Trim().ToLowerInvariant() switch
        {
            "web" => "web",
            "pwn" => "pwn",
            "ai" => "ai",
            "crypto" => "crypto",
            "reverse" or "rev" => "reverse",
            _ => "misc"
        };
    }

    private static string MapCompetitionStatus(CompetitionStatus status)
        => status switch
        {
            CompetitionStatus.Running => "running",
            CompetitionStatus.Paused => "paused",
            CompetitionStatus.Finished => "ended",
            _ => "pending"
        };

    private static string MapPhase(CompetitionStatus competitionStatus, AwdpRoundStatus? roundStatus)
    {
        if (competitionStatus == CompetitionStatus.Finished)
            return "ended";
        return roundStatus switch
        {
            AwdpRoundStatus.RoundRunning => "running",
            AwdpRoundStatus.RoundScoring => "settling",
            AwdpRoundStatus.RoundFinished => "waiting",
            _ => "waiting"
        };
    }

    private static DateTime? ResolveRoundEnd(Competition competition, AwdpRound? round)
    {
        if (round is null)
            return null;
        if (round.EndTime is not null)
            return round.EndTime;
        return round.Status == AwdpRoundStatus.RoundRunning
            ? round.StartTime.AddSeconds(competition.RoundDurationSeconds ?? 300)
            : null;
    }
}
