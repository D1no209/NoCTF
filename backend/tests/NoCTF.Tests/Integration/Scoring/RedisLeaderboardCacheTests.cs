using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Scoring;

[Category("Integration")]
[NotInParallel]
public sealed class RedisLeaderboardCacheTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Historical_snapshot_uses_the_exact_projection_cutoff(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_historical_cutoff", async fixture =>
        {
            await AddTeamsAsync(fixture.Options, fixture.CompetitionId, cancellationToken);
            var engine = new CapturingProjectionEngine();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(db, engine, new RecordingPublisher(), fixture.Redis);

            await cache.CreateAsync(
                fixture.CompetitionId,
                DateTimeOffset.Parse("2026-07-31T00:00:30Z"),
                historical: true,
                cancellationToken);
            await Assert.That(engine.Teams).IsEmpty();

            await cache.CreateAsync(
                fixture.CompetitionId,
                DateTimeOffset.Parse("2026-07-31T00:02:00Z"),
                historical: true,
                cancellationToken);
            await Assert.That(engine.Teams.Select(team => team.Name))
                .IsEquivalentTo(["Approved team"]);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Older_projection_cannot_overwrite_a_newer_snapshot(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_old_snapshot", async fixture =>
        {
            var engine = new BlockingProjectionEngine();
            var publisher = new RecordingPublisher();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(db, engine, publisher, fixture.Redis);
            var refresh = Task.Run(
                () => cache.RefreshAsync(fixture.CompetitionId, cancellationToken),
                cancellationToken);
            await engine.Entered.WaitAsync(cancellationToken);
            var newer = Snapshot(fixture.CompetitionId, revision: 2);
            try
            {
                await fixture.Database.HashSetAsync(
                    StateKey(fixture.CompetitionId),
                    [
                        new("snapshotRevision", 2),
                        new("snapshot", JsonSerializer.Serialize(newer, JsonOptions))
                    ]);
            }
            finally
            {
                engine.Release();
            }
            await refresh;

            var state = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["snapshotRevision", "snapshot"]);
            await Assert.That(state[0].ToString()).IsEqualTo("2");
            var stored = JsonSerializer.Deserialize<LeaderboardResponse>(
                state[1].ToString(),
                JsonOptions);
            await Assert.That(stored?.SnapshotRevision).IsEqualTo(2);
            await Assert.That(stored?.GeneratedAt).IsEqualTo(newer.GeneratedAt);
            await Assert.That(publisher.Notifications).IsEmpty();
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Equal_revision_projection_is_idempotent(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_equal_snapshot", async fixture =>
        {
            var engine = new BlockingProjectionEngine();
            var publisher = new RecordingPublisher();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(db, engine, publisher, fixture.Redis);
            var refresh = Task.Run(
                () => cache.RefreshAsync(fixture.CompetitionId, cancellationToken),
                cancellationToken);
            await engine.Entered.WaitAsync(cancellationToken);
            var winner = Snapshot(fixture.CompetitionId, revision: 1);
            var payload = JsonSerializer.Serialize(winner, JsonOptions);
            try
            {
                await fixture.Database.HashSetAsync(
                    StateKey(fixture.CompetitionId),
                    [
                        new("snapshotRevision", 1),
                        new("snapshot", payload)
                    ]);
            }
            finally
            {
                engine.Release();
            }
            await refresh;
            await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);

            var state = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["snapshotRevision", "snapshot"]);
            await Assert.That(state[0].ToString()).IsEqualTo("1");
            await Assert.That(state[1].ToString()).IsEqualTo(payload);
            await Assert.That(engine.InvocationCount).IsEqualTo(1);
            await Assert.That(publisher.Notifications).IsEmpty();
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Dirty_and_failure_revisions_are_only_cleared_by_an_equal_or_newer_snapshot(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_monotonic_state", async fixture =>
        {
            var publisher = new RecordingPublisher();
            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 2, cancellationToken);
            await using (var invalidateDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    invalidateDb,
                    new EmptyProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await cache.InvalidateAsync(fixture.CompetitionId, cancellationToken);
            }
            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 3, cancellationToken);
            await using (var invalidateDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    invalidateDb,
                    new EmptyProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await cache.InvalidateAsync(fixture.CompetitionId, cancellationToken);
            }
            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 1, cancellationToken);
            await using (var invalidateDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    invalidateDb,
                    new EmptyProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await cache.InvalidateAsync(fixture.CompetitionId, cancellationToken);
            }

            var failureAt = "2026-07-31T00:00:00.0000000+00:00";
            await fixture.Database.HashSetAsync(
                StateKey(fixture.CompetitionId),
                [
                    new("failureRevision", 3),
                    new("failureAt", failureAt)
                ]);
            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 2, cancellationToken);
            await using (var staleDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    staleDb,
                    new EmptyProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);
            }

            var staleState = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["snapshotRevision", "dirtyRevision", "failureRevision", "failureAt"]);
            await Assert.That(staleState[0].ToString()).IsEqualTo("2");
            await Assert.That(staleState[1].ToString()).IsEqualTo("3");
            await Assert.That(staleState[2].ToString()).IsEqualTo("3");
            await Assert.That(staleState[3].ToString()).IsEqualTo(failureAt);

            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 3, cancellationToken);
            await using (var currentDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    currentDb,
                    new EmptyProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);
            }

            var currentState = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["snapshotRevision", "dirtyRevision", "failureRevision", "failureAt"]);
            await Assert.That(currentState[0].ToString()).IsEqualTo("3");
            await Assert.That(currentState[1].IsNull).IsTrue();
            await Assert.That(currentState[2].IsNull).IsTrue();
            await Assert.That(currentState[3].IsNull).IsTrue();
            await Assert.That(publisher.Notifications).Count().IsEqualTo(2);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Older_projection_failure_cannot_replace_a_newer_failure(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_monotonic_failure", async fixture =>
        {
            var publisher = new RecordingPublisher();
            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 3, cancellationToken);
            await using (var newerDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    newerDb,
                    new ThrowingProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await AssertRefreshFailsAsync(
                    cache,
                    fixture.CompetitionId,
                    cancellationToken);
            }
            var newerFailure = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["failureRevision", "failureAt"]);
            await Assert.That(newerFailure[0].ToString()).IsEqualTo("3");
            await Assert.That(newerFailure[1].IsNullOrEmpty).IsFalse();

            await SetRevisionAsync(fixture.Options, fixture.CompetitionId, 2, cancellationToken);
            await using (var olderDb = new NoCtfDbContext(fixture.Options))
            {
                var cache = CreateCache(
                    olderDb,
                    new ThrowingProjectionEngine(),
                    publisher,
                    fixture.Redis);
                await AssertRefreshFailsAsync(
                    cache,
                    fixture.CompetitionId,
                    cancellationToken);
            }
            var finalFailure = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["failureRevision", "failureAt"]);
            await Assert.That(finalFailure[0].ToString()).IsEqualTo("3");
            await Assert.That(finalFailure[1].ToString())
                .IsEqualTo(newerFailure[1].ToString());
            await Assert.That(publisher.Notifications).IsEmpty();
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_refreshes_project_once_under_the_competition_lock(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_single_flight", async fixture =>
        {
            var engine = new BlockingProjectionEngine();
            var publisher = new RecordingPublisher();
            await using var firstDb = new NoCtfDbContext(fixture.Options);
            await using var secondDb = new NoCtfDbContext(fixture.Options);
            var firstCache = CreateCache(firstDb, engine, publisher, fixture.Redis);
            var secondCache = CreateCache(secondDb, engine, publisher, fixture.Redis);
            var first = Task.Run(
                () => firstCache.RefreshAsync(fixture.CompetitionId, cancellationToken),
                cancellationToken);
            await engine.Entered.WaitAsync(cancellationToken);
            var second = Task.Run(
                () => secondCache.RefreshAsync(fixture.CompetitionId, cancellationToken),
                cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

            engine.Release();
            await Task.WhenAll(first, second);

            await Assert.That(engine.InvocationCount).IsEqualTo(1);
            await Assert.That(publisher.Notifications).Count().IsEqualTo(1);
            var revision = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                "snapshotRevision");
            await Assert.That(revision.ToString()).IsEqualTo("1");
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Revisions_above_the_lua_safe_integer_range_remain_exact(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_large_revision", async fixture =>
        {
            const long lowerRevision = 9_007_199_254_740_992;
            const long higherRevision = 9_007_199_254_740_993;
            var lowerSnapshot = new LeaderboardResponse(
                fixture.CompetitionId,
                DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
                [])
            {
                SnapshotRevision = lowerRevision,
                TargetRevision = lowerRevision
            };
            await fixture.Database.HashSetAsync(
                StateKey(fixture.CompetitionId),
                [
                    new("snapshotRevision", lowerRevision),
                    new("snapshot", JsonSerializer.Serialize(lowerSnapshot, JsonOptions)),
                    new("dirtyRevision", lowerRevision)
                ]);
            await SetRevisionAsync(
                fixture.Options,
                fixture.CompetitionId,
                higherRevision,
                cancellationToken);
            var publisher = new RecordingPublisher();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(
                db,
                new EmptyProjectionEngine(),
                publisher,
                fixture.Redis);

            await cache.InvalidateAsync(fixture.CompetitionId, cancellationToken);
            var dirty = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                "dirtyRevision");
            await Assert.That(dirty.ToString()).IsEqualTo(higherRevision.ToString());

            await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);

            var state = await fixture.Database.HashGetAsync(
                StateKey(fixture.CompetitionId),
                ["snapshotRevision", "dirtyRevision"]);
            await Assert.That(state[0].ToString()).IsEqualTo(higherRevision.ToString());
            await Assert.That(state[1].IsNull).IsTrue();
            await Assert.That(publisher.Notifications).Count().IsEqualTo(1);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Projection_only_receives_approved_teams(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_approved_teams", async fixture =>
        {
            await AddTeamsAsync(fixture.Options, fixture.CompetitionId, cancellationToken);
            var engine = new CapturingProjectionEngine();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(
                db,
                engine,
                new RecordingPublisher(),
                fixture.Redis);

            await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);

            await Assert.That(engine.Teams).Count().IsEqualTo(1);
            await Assert.That(engine.Teams[0].Name).IsEqualTo("Approved team");
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Ranked_blood_summaries_round_trip_through_the_snapshot(
        CancellationToken cancellationToken)
    {
        await RunAsync("noctf_leaderboard_bloods", async fixture =>
        {
            var occurredAt = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
            var challengeId = Guid.NewGuid();
            var teams = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var bloods = teams.Select((teamId, index) => new LeaderboardBloodSummary(
                $"challenge:{challengeId:N}",
                LeaderboardSlotKind.Challenge,
                (LeaderboardBloodRank)(index + 1),
                teamId,
                $"team-{index + 1}",
                occurredAt.AddSeconds(index)))
                .ToList();
            await using var db = new NoCtfDbContext(fixture.Options);
            var cache = CreateCache(
                db,
                new FixedProjectionEngine(bloods),
                new RecordingPublisher(),
                fixture.Redis);

            await cache.RefreshAsync(fixture.CompetitionId, cancellationToken);
            var snapshot = await cache.GetAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(snapshot).IsNotNull();
            await Assert.That(snapshot!.Bloods).Count().IsEqualTo(3);
            for (var index = 0; index < bloods.Count; index++)
            {
                await Assert.That(snapshot.Bloods[index])
                    .IsEqualTo(bloods[index]);
            }
        }, cancellationToken);
    }

    private static RedisLeaderboardCache CreateCache(
        NoCtfDbContext db,
        ILeaderboardProjectionEngine engine,
        ILeaderboardRefreshPublisher publisher,
        IConnectionMultiplexer redis) =>
        new(
            db,
            engine,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Leaderboard:CacheTtlSeconds"] = "300"
                })
                .Build(),
            publisher,
            redis);

    private static async Task AssertRefreshFailsAsync(
        RedisLeaderboardCache cache,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var failed = false;
        try
        {
            await cache.RefreshAsync(competitionId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        await Assert.That(failed).IsTrue();
    }

    private static async Task SetRevisionAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Competitions
            .Where(competition => competition.Id == competitionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    competition => competition.LeaderboardRevision,
                    revision),
                cancellationToken);
    }

    private static async Task AddTeamsAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var now = DateTimeOffset.Parse("2026-07-31T00:01:00Z");
        var registrations = new[]
        {
            (Name: "Approved team", Status: TeamRegistrationStatus.Approved, Token: 'a'),
            (Name: "Pending team", Status: TeamRegistrationStatus.Pending, Token: 'p'),
            (Name: "Rejected team", Status: TeamRegistrationStatus.Rejected, Token: 'r')
        };
        foreach (var registration in registrations)
        {
            var userId = Guid.CreateVersion7();
            var normalized = registration.Name.ToUpperInvariant().Replace(' ', '-');
            db.Users.Add(new User
            {
                Id = userId,
                UserName = normalized,
                NormalizedUserName = normalized,
                Email = $"{normalized}@example.test",
                NormalizedEmail = $"{normalized}@EXAMPLE.TEST",
                PasswordHash = "test",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Teams.Add(new Team
            {
                Id = Guid.CreateVersion7(),
                CompetitionId = competitionId,
                Name = registration.Name,
                NormalizedName = registration.Name.ToUpperInvariant(),
                CaptainId = userId,
                MemberIds = [userId],
                InvitationToken = new string(registration.Token, 32),
                RegistrationStatus = registration.Status,
                RegisteredAt = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static LeaderboardResponse Snapshot(Guid competitionId, long revision) =>
        new(
            competitionId,
            DateTimeOffset.Parse($"2026-07-31T00:00:0{revision}+00:00"),
            [])
        {
            SnapshotRevision = revision,
            TargetRevision = revision
        };

    private static RedisKey StateKey(Guid competitionId) =>
        $"leaderboard:v2:{{{competitionId:N}}}:state";

    private static async Task RunAsync(
        string databaseName,
        Func<Fixture, Task> test,
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase(databaseName)
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder("redis:7-alpine").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var competitionId = await SeedAsync(options, cancellationToken);
            await test(new(
                options,
                competitionId,
                redis,
                redis.GetDatabase()));
        });
    }

    private static async Task<Guid> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(1));
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "leaderboard-owner",
            NormalizedUserName = "LEADERBOARD-OWNER",
            Email = "leaderboard-owner@example.test",
            NormalizedEmail = "LEADERBOARD-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Redis leaderboard cache",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            ConfigurationJson = "{}",
            ConfigurationUpdatedAt = now,
            LeaderboardRevision = 1,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return competitionId;
    }

    private sealed record Fixture(
        DbContextOptions<NoCtfDbContext> Options,
        Guid CompetitionId,
        IConnectionMultiplexer Redis,
        IDatabase Database);

    private sealed class EmptyProjectionEngine : ILeaderboardProjectionEngine
    {
        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input) =>
            new([], [], []);
    }

    private sealed class CapturingProjectionEngine : ILeaderboardProjectionEngine
    {
        public IReadOnlyList<LeaderboardTeamFact> Teams { get; private set; } = [];

        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input)
        {
            Teams = input.Teams;
            return new([], [], []);
        }
    }

    private sealed class FixedProjectionEngine(
        IReadOnlyList<LeaderboardBloodSummary> bloods)
        : ILeaderboardProjectionEngine
    {
        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input) =>
            new([], [], bloods);
    }

    private sealed class ThrowingProjectionEngine : ILeaderboardProjectionEngine
    {
        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input) =>
            throw new InvalidOperationException("Projection failed.");
    }

    private sealed class BlockingProjectionEngine : ILeaderboardProjectionEngine
    {
        private readonly TaskCompletionSource<bool> entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int invocationCount;

        public Task Entered => entered.Task;
        public int InvocationCount => Volatile.Read(ref invocationCount);

        public void Release() => release.TrySetResult(true);

        public LeaderboardProjectionResult Project(LeaderboardProjectionInput input)
        {
            Interlocked.Increment(ref invocationCount);
            entered.TrySetResult(true);
            release.Task.GetAwaiter().GetResult();
            return new([], [], []);
        }
    }

    private sealed class RecordingPublisher : ILeaderboardRefreshPublisher
    {
        public ConcurrentQueue<(Guid CompetitionId, DateTimeOffset GeneratedAt)> Notifications { get; } = new();

        public Task PublishAsync(
            Guid competitionId,
            DateTimeOffset generatedAt,
            CancellationToken cancellationToken)
        {
            Notifications.Enqueue((competitionId, generatedAt));
            return Task.CompletedTask;
        }
    }
}
