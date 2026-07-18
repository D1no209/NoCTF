using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class AwdpScreenSnapshotCacheTests
{
    [Fact]
    public async Task Cache_KeepsRandomAnonymousKeysWithinFixedCapacity()
    {
        var cache = new AwdpScreenSnapshotCache(
            (_, _) => Task.FromResult<AwdpScreenSnapshotDto?>(null),
            capacity: 4,
            stripeCount: 2,
            cacheDuration: TimeSpan.FromMinutes(1),
            entryRetention: TimeSpan.FromMinutes(1));

        for (var i = 0; i < 100; i++)
            Assert.Null(await cache.GetAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(4, cache.CachedEntryCount);
        Assert.Equal(2, cache.StripeCount);
    }

    [Fact]
    public async Task Cache_CoalescesConcurrentBuildsForOneCompetition()
    {
        var competitionId = Guid.NewGuid();
        var buildCount = 0;
        var cache = new AwdpScreenSnapshotCache(
            async (_, ct) =>
            {
                Interlocked.Increment(ref buildCount);
                await Task.Delay(20, ct);
                return Snapshot(competitionId, DateTime.UtcNow, score: 10);
            },
            capacity: 4,
            stripeCount: 2,
            cacheDuration: TimeSpan.FromMinutes(1),
            entryRetention: TimeSpan.FromMinutes(1));

        var snapshots = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => cache.GetAsync(competitionId, CancellationToken.None)));

        Assert.All(snapshots, Assert.NotNull);
        Assert.Equal(1, buildCount);
    }

    [Fact]
    public async Task Cache_RebuildsAfterFreshnessExpiration()
    {
        var competitionId = Guid.NewGuid();
        var buildCount = 0;
        var cache = new AwdpScreenSnapshotCache(
            (_, _) => Task.FromResult<AwdpScreenSnapshotDto?>(
                Snapshot(competitionId, DateTime.UtcNow, Interlocked.Increment(ref buildCount))),
            capacity: 4,
            stripeCount: 2,
            cacheDuration: TimeSpan.FromMilliseconds(20),
            entryRetention: TimeSpan.FromMinutes(1));

        await cache.GetAsync(competitionId, CancellationToken.None);
        await Task.Delay(100);
        var refreshed = await cache.GetAsync(competitionId, CancellationToken.None);

        Assert.Equal(2, buildCount);
        Assert.Equal(2, Assert.Single(refreshed!.Scoreboard).TotalScore);
    }

    [Fact]
    public void CachedSnapshot_VersionIgnoresServerClockButTracksContentChanges()
    {
        var competitionId = Guid.NewGuid();
        var first = AwdpScreenSnapshotCache.CreateCachedSnapshot(
            Snapshot(competitionId, new DateTime(2026, 7, 12, 1, 0, 0, DateTimeKind.Utc), score: 10));
        var clockOnly = AwdpScreenSnapshotCache.CreateCachedSnapshot(
            Snapshot(competitionId, new DateTime(2026, 7, 12, 1, 0, 3, DateTimeKind.Utc), score: 10));
        var changed = AwdpScreenSnapshotCache.CreateCachedSnapshot(
            Snapshot(competitionId, new DateTime(2026, 7, 12, 1, 0, 3, DateTimeKind.Utc), score: 11));

        Assert.Equal(first.Version, clockOnly.Version);
        Assert.NotEqual(first.Version, changed.Version);
        Assert.StartsWith("data: {", System.Text.Encoding.UTF8.GetString(first.SsePayload.Span));
        Assert.EndsWith("\n\n", System.Text.Encoding.UTF8.GetString(first.SsePayload.Span));
    }

    [Fact]
    public async Task SnapshotBuilder_UsesCurrentRoundRowsAndAggregatedHistory()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var now = DateTime.UtcNow;
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "AWDP",
            GameModeType = GameModeType.Awdp,
            Status = CompetitionStatus.Running,
            StartTime = now.AddHours(-1),
            EndTime = now.AddHours(1),
            OwnerId = Guid.NewGuid()
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Team",
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Challenge",
            TypeId = "web",
            Direction = "web",
            CreatedAt = now
        });
        db.AwdpRounds.AddRange(
            new AwdpRound
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                RoundNumber = 1,
                StartTime = now.AddMinutes(-10),
                EndTime = now.AddMinutes(-5),
                Status = AwdpRoundStatus.RoundFinished
            },
            new AwdpRound
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                RoundNumber = 2,
                StartTime = now.AddMinutes(-5),
                Status = AwdpRoundStatus.RoundRunning
            });
        db.AwdpRoundScores.AddRange(
            RoundScore(competitionId, teamId, challengeId, 1, attack: 10, defense: 20, total: 30, now),
            RoundScore(competitionId, teamId, challengeId, 2, attack: 3, defense: 4, total: 7, now));
        await db.SaveChangesAsync();

        var snapshot = await AwdpScreenSnapshotBuilder.BuildAsync(db, competitionId, CancellationToken.None);

        var team = Assert.Single(snapshot!.Scoreboard);
        Assert.Equal(13, team.AttackScore);
        Assert.Equal(24, team.DefenseScore);
        Assert.Equal(7, team.CurrentRoundScore);
        Assert.Equal(2, snapshot.RoundTimeline.Count);
        Assert.Equal(37, snapshot.Stats.TotalScoreDelta);
    }

    private static AwdpScreenSnapshotDto Snapshot(Guid competitionId, DateTime serverTime, int score)
        => new()
        {
            Game = new AwdpScreenGameDto
            {
                Id = competitionId,
                Title = "Game",
                Status = "running",
                ServerTime = serverTime
            },
            Scoreboard =
            [
                new AwdpScreenTeamScoreDto
                {
                    TeamId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    TeamName = "Team",
                    Rank = 1,
                    TotalScore = score
                }
            ]
        };

    private static AwdpRoundScore RoundScore(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        int round,
        int attack,
        int defense,
        int total,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = round,
            AttackScoreDelta = attack,
            DefenseScoreDelta = defense,
            RoundScoreDelta = total,
            CreatedAt = now
        };

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new AwdpScreenTenantContext(competitionId));
    }
}

file sealed class AwdpScreenTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
