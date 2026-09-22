using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Worker;
using Npgsql;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Awdp_configuration_event_invalidates_cached_schema_and_publishes_recomputed_rounds(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.Parse("2026-09-08T00:02:10Z");
            var clock = new FakeTimeProvider(now);
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Awdp, 0, owner.Id, now);
            fixture.Competition.StartAt = now.AddSeconds(-130);
            string Configuration(int duration) => JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion, duration, new(100, 100, 2, ScoreDecayMode.Fixed),
                new(40, 40, 2, ScoreDecayMode.Fixed), RequireBreakBeforeFix: false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            fixture.Competition.ConfigurationJson = Configuration(60);
            var fact = fixture.Facts[0];
            fact.OccurredAt = fixture.Competition.StartAt.AddSeconds(10);
            fact.UpdatedAt = fact.OccurredAt;
            fact.Result = GameplayFactResult.Correct;
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.Add(fact);
            await db.SaveChangesAsync(ct);
            using var cacheServices = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
            var publisher = Substitute.For<ILeaderboardRefreshPublisher>();
            var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                publisher, cacheServices.GetRequiredService<IFusionCacheProvider>(), clock: clock);
            var before = (await cache.GetScoreboardAsync(fixture.Competition.Id, ct))!;
            var outbox = new NoCTF.Infrastructure.Messaging.NoOpTransactionalMessageOutbox();
            var store = new NoCTF.Infrastructure.Competitions.Configuration.CompetitionConfigurationStore(db, outbox,
                new NoCTF.Infrastructure.Competitions.Events.CompetitionEventStore(db, outbox));
            await store.TryUpdateAsync(fixture.Competition.Id, Configuration(120), true, now, ct);
            var change = await db.CompetitionEvents.AsNoTracking().SingleAsync(ct);
            var queue = new NoCTF.Infrastructure.Messaging.LeaderboardProjectionMergeQueue();
            await new CompetitionEventLeaderboardMessageHandler(cache, queue, clock).Handle(new(
                change.CompetitionId, change.Id, change.Kind, change.Level, change.OccurredAt), ct);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Assert.That(queue.TakeDue(clock.GetUtcNow())).IsEquivalentTo([fixture.Competition.Id]);
            await new LeaderboardMessageHandler(cache).Handle(new(fixture.Competition.Id), ct);
            var after = (await cache.GetScoreboardAsync(fixture.Competition.Id, ct))!;
            await Assert.That(before.Schema.LatestRound).IsEqualTo(3);
            await Assert.That(after.Schema.LatestRound).IsEqualTo(2);
            await Assert.That(after.Schema.Revision).IsNotEqualTo(before.Schema.Revision);
            await Assert.That(after.Snapshot.Version).IsGreaterThan(before.Snapshot.Version);
            await Assert.That(after.Snapshot.Teams.Single().TotalScore).IsEqualTo(100);
            await publisher.Received().PublishAsync(Arg.Is<ScoreboardProjection>(value =>
                value != null && value.Schema.Revision == after.Schema.Revision), ct);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awdp_cache_rebuilds_after_round_boundary_without_a_business_event(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_projection_awdp_boundary")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-24T00:00:01Z");
            var clock = new FakeTimeProvider(now);
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Awdp, 0, owner.Id, now);
            fixture.Competition.StartAt = now.AddSeconds(-1);
            fixture.Competition.EndAt = now.AddHours(1);
            fixture.Competition.ConfigurationJson = JsonSerializer.Serialize(
                new AwdpConfiguration(
                    AwdpConfiguration.CurrentSchemaVersion,
                    2,
                    ScoreCurveConfiguration.Default,
                    ScoreCurveConfiguration.Default,
                    RequireBreakBeforeFix: false),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(fixture.Facts);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var publisher = Substitute.For<ILeaderboardRefreshPublisher>();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                publisher,
                cacheServices.GetRequiredService<IFusionCacheProvider>(),
                new RedisLeaderboardPublicationFence(redis),
                new LeaderboardProjectionKeyedLock(),
                clock);

            var first = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);
            await Assert.That(first).IsNotNull();
            await Assert.That(first!.Schema.Rounds[^1].Number).IsEqualTo(1);

            clock.Advance(TimeSpan.FromSeconds(2));
            var second = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);

            await Assert.That(second).IsNotNull();
            await Assert.That(second!.Schema.Rounds[^1].Number).IsEqualTo(2);
            await publisher.Received(2).PublishAsync(
                Arg.Any<ScoreboardProjection>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_cache_misses_and_complete_cache_loss_rebuild_from_PostgreSQL_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_projection_cache_loss")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redisContainer = new RedisBuilder(
                "redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redisContainer.StartAsync(cancellationToken));
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                redisContainer.GetConnectionString());

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(fixture.Facts);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cacheProvider = cacheServices.GetRequiredService<IFusionCacheProvider>();
            var publisher = Substitute.For<ILeaderboardRefreshPublisher>();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                publisher,
                cacheProvider,
                new RedisLeaderboardPublicationFence(redis),
                new LeaderboardProjectionKeyedLock());

            var initialReads = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => cache.GetScoreboardAsync(
                        fixture.Competition.Id,
                        cancellationToken)));

            await Assert.That(initialReads.All(item => item is not null)).IsTrue();
            await publisher.Received(1).PublishAsync(
                Arg.Any<ScoreboardProjection>(),
                Arg.Any<CancellationToken>());

            await redis.GetDatabase().ExecuteAsync("FLUSHDB");
            await cacheProvider.GetCache(NoCtfCacheNames.Leaderboards).RemoveAsync(
                $"projection:v2:{fixture.Competition.Id:N}",
                token: cancellationToken);

            var rebuilt = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);

            await Assert.That(rebuilt).IsNotNull();
            await Assert.That(rebuilt!.Snapshot.Teams).IsNotEmpty();
            await publisher.Received(2).PublishAsync(
                Arg.Any<ScoreboardProjection>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Publication_runs_after_projection_lock_release_and_failure_requeues_projection(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_projection_publication_failure")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(fixture.Facts);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var fence = new LockObservingFailingPublicationFence(
                postgres.GetConnectionString(),
                fixture.Competition.Id);
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>(),
                fence);

            await Assert.That(async () =>
                    await cache.RefreshAsync(fixture.Competition.Id, cancellationToken))
                .Throws<InvalidOperationException>();
            await Assert.That(fence.ProjectionLockWasAvailable).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_projection_commit_preserves_the_last_successful_cache(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_projection_commit_failure")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var commitFailure = new FailingCommitInterceptor();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(commitFailure)
                .Options;
            var now = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
            var fact = fixture.Facts[0];
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.Add(fact);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());
            await cache.RefreshAsync(fixture.Competition.Id, cancellationToken);
            var successful = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);

            fact.Result = GameplayFactResult.Correct;
            fact.UpdatedAt = now.AddMinutes(1);
            await db.SaveChangesAsync(cancellationToken);
            commitFailure.FailNextCommit();

            await Assert.That(async () =>
                    await cache.RefreshAsync(fixture.Competition.Id, cancellationToken))
                .Throws<InvalidOperationException>();
            var retained = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);

            await Assert.That(successful).IsNotNull();
            await Assert.That(retained).IsNotNull();
            await Assert.That(retained!.Snapshot.Version).IsEqualTo(successful!.Snapshot.Version);
            await Assert.That(retained.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(successful.Snapshot.Teams.Single().TotalScore);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Participant_projection_excludes_unpublished_challenge_scores(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_participant_scoreboard_visibility")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var projectedAt = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(projectedAt);
            var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, projectedAt);
            var visibleFact = fixture.Facts[0];
            visibleFact.Result = GameplayFactResult.Correct;
            var hiddenTemplate = new Challenge
            {
                Id = Guid.CreateVersion7(projectedAt.AddMinutes(40)),
                OwnerId = owner.Id,
                Mode = GameMode.Ctf,
                Visibility = ChallengeVisibility.Private,
                Title = "Hidden challenge",
                Direction = "Pwn",
                DefinitionJson = fixture.Challenge.DefinitionJson,
                CreatedAt = projectedAt.AddMinutes(-20),
                UpdatedAt = projectedAt.AddMinutes(-20)
            };
            var hiddenChallenge = new CompetitionChallenge
            {
                Id = Guid.CreateVersion7(projectedAt.AddMinutes(41)),
                CompetitionId = fixture.Competition.Id,
                ChallengeId = hiddenTemplate.Id,
                Order = 2,
                IsPublished = false,
                RulesJson = fixture.CompetitionChallenge.RulesJson,
                UpdatedAt = projectedAt.AddMinutes(-20)
            };
            var hiddenFact = new GameplayFact
            {
                Id = Guid.CreateVersion7(projectedAt.AddMinutes(-10)),
                CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = hiddenChallenge.Id,
                TeamId = fixture.Team.Id,
                ActorUserId = owner.Id,
                Kind = GameplayFactKind.FlagAttempt,
                OccurredAt = projectedAt.AddMinutes(-10),
                Value = "flag{hidden}",
                ValueSha256 = new byte[32],
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Correct,
                UpdatedAt = projectedAt.AddMinutes(-10)
            };
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.AddRange(fixture.Challenge, hiddenTemplate);
            db.CompetitionChallenges.AddRange(fixture.CompetitionChallenge, hiddenChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(visibleFact, hiddenFact);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            var full = await cache.CreateScoreboardAsync(
                fixture.Competition.Id,
                projectedAt,
                cancellationToken);

            await Assert.That(full).IsNotNull();
            await Assert.That(full!.ParticipantView).IsNotNull();
            var participant = full.ParticipantView!.ToProjection();
            await Assert.That(full.Snapshot.Teams.Single().Achievements!.Count).IsEqualTo(2);
            var visibleAchievement = participant.Snapshot.Teams.Single().Achievements!.Single();
            await Assert.That(visibleAchievement.CompetitionChallengeId).IsEqualTo(fixture.CompetitionChallenge.Id);
            await Assert.That(full.Snapshot.Teams.Single().Achievements!.Single(x => x.CompetitionChallengeId == hiddenChallenge.Id).DisplayName)
                .IsEqualTo(owner.UserName);
            await Assert.That(full.ChallengeCatalog.Challenges).Count().IsEqualTo(2);
            await Assert.That(participant.ChallengeCatalog.Challenges).HasSingleItem();
            await Assert.That(participant.ChallengeCatalog.Challenges[0].CompetitionChallengeId)
                .IsEqualTo(fixture.CompetitionChallenge.Id);
            var fullTeam = full.Snapshot.Teams.Single();
            var participantTeam = participant.Snapshot.Teams.Single();
            await Assert.That(participantTeam.TotalScore).IsLessThan(fullTeam.TotalScore);
            await Assert.That(participantTeam.TotalScore).IsEqualTo(checked(
                participantTeam.Slots.Sum(slot => slot.NetPoints.GetValueOrDefault())
                + participantTeam.ScoreOutsideWindow
                + participantTeam.GlobalAdjustments.Sum(adjustment => adjustment.NetPoints)));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awdp_long_history_uses_a_bounded_round_window_without_losing_total_score(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_bounded_round_window")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var projectedAt = DateTimeOffset.UtcNow;
            var startedAt = projectedAt.AddDays(-30);
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(projectedAt);
            var fixture = CreateFixture(GameMode.Awdp, 0, owner.Id, projectedAt);
            fixture.Competition.StartAt = startedAt;
            fixture.Competition.ConfigurationJson = JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                1,
                new(100, 100, 2, ScoreDecayMode.Fixed),
                new(40, 40, 2, ScoreDecayMode.Fixed),
                RequireBreakBeforeFix: false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var firstBreak = fixture.Facts[0];
            firstBreak.OccurredAt = startedAt.AddSeconds(1);
            firstBreak.UpdatedAt = firstBreak.OccurredAt;
            firstBreak.Result = GameplayFactResult.Correct;
            var laterWrongBreak = new GameplayFact
            {
                Id = Guid.CreateVersion7(startedAt.AddSeconds(199)),
                CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                TeamId = fixture.Team.Id,
                ActorUserId = owner.Id,
                Kind = GameplayFactKind.BreakAttempt,
                OccurredAt = startedAt.AddSeconds(199),
                Value = "flag{later-wrong}",
                ValueSha256 = new byte[32],
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong,
                FailureCode = GameplayFactFailureCode.AwdpPatchFailed,
                UpdatedAt = startedAt.AddSeconds(199)
            };
            var earlyAdjustment = CreateManualAdjustment(
                fixture,
                owner.Id,
                startedAt.AddSeconds(9),
                "7");
            var laterAdjustment = CreateManualAdjustment(
                fixture,
                owner.Id,
                startedAt.AddSeconds(199),
                "7");

            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(firstBreak, laterWrongBreak, earlyAdjustment, laterAdjustment);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            var latest = await cache.CreateBundleAsync(
                fixture.Competition.Id,
                projectedAt,
                cancellationToken);
            var elapsedSeconds = (int)(projectedAt - startedAt).TotalSeconds;
            await Assert.That(latest).IsNotNull();
            await Assert.That(latest!.Scoreboard.Schema.Rounds)
                .Count().IsEqualTo(ScoreboardRoundWindow.DefaultSize);
            await Assert.That(latest.Scoreboard.Schema.RoundWindowEnd)
                .IsEqualTo(elapsedSeconds + 1);
            await Assert.That(latest.Scoreboard.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(100L * (elapsedSeconds - 1) + 14);
            await Assert.That(latest.Scoreboard.Snapshot.Teams.Single().ScoreOutsideWindow)
                .IsGreaterThan(0);
            var historicalAchievement = latest.Scoreboard.Snapshot.Teams.Single().Achievements!.Single();
            await Assert.That(historicalAchievement.Kind).IsEqualTo(ScoreboardEntryKind.Attack);
            await Assert.That(historicalAchievement.OccurredAt.UtcTicks / 10).IsEqualTo(firstBreak.OccurredAt.UtcTicks / 10);

            fixture.Competition.Status = CompetitionStatus.Finished;
            fixture.Competition.UpdatedAt = projectedAt.AddMinutes(1);
            db.CompetitionEvents.AddRange(
                LifecycleEvent(
                    fixture.Competition.Id,
                    CompetitionStatus.Published,
                    CompetitionStatus.Running,
                    startedAt),
                LifecycleEvent(
                    fixture.Competition.Id,
                    CompetitionStatus.Running,
                    CompetitionStatus.Finished,
                    projectedAt.AddMinutes(1)));
            await db.SaveChangesAsync(cancellationToken);

            var asOfLatest = await cache.CreateScoreboardWindowAsync(
                fixture.Competition.Id,
                elapsedSeconds + 1,
                projectedAt,
                cancellationToken);
            await Assert.That(asOfLatest!.Schema.RoundWindowEnd).IsEqualTo(elapsedSeconds + 1);
            await Assert.That(asOfLatest.Schema.Rounds[^1].State)
                .IsEqualTo(ScoreboardRoundState.Running);

            var historical = await cache.CreateScoreboardWindowAsync(
                fixture.Competition.Id,
                100,
                projectedAt,
                cancellationToken);
            await Assert.That(historical).IsNotNull();
            await Assert.That(historical!.Schema.Rounds.Select(round => round.Number))
                .IsEquivalentTo(Enumerable.Range(51, ScoreboardRoundWindow.DefaultSize));
            await Assert.That(historical.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(latest.Scoreboard.Snapshot.Teams.Single().TotalScore);
            await Assert.That(historical.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(checked(
                    historical.Snapshot.Teams.Single().ScoreOutsideWindow
                    + historical.Snapshot.Teams.Single().Slots.Sum(slot => slot.NetPoints ?? 0)
                    + historical.Snapshot.Teams.Single().GlobalAdjustments.Sum(item => item.NetPoints)));
            await Assert.That(historical.Snapshot.Teams.Single().GlobalAdjustmentCount).IsEqualTo(2);
            await Assert.That(historical.Snapshot.Teams.Single().GlobalAdjustments.Single().NetPoints)
                .IsEqualTo(14);
            await Assert.That(historical.Snapshot.Teams.Single().Slots.Sum(slot => slot.EntryCount))
                .IsEqualTo(ScoreboardRoundWindow.DefaultSize);

            static CompetitionEvent LifecycleEvent(
                Guid competitionId,
                CompetitionStatus from,
                CompetitionStatus to,
                DateTimeOffset occurredAt) => new()
                {
                    Id = Guid.CreateVersion7(occurredAt),
                    CompetitionId = competitionId,
                    Kind = CompetitionEventKind.CompetitionLifecycleChanged,
                    Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Public,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        competitionStatus = to,
                        from,
                        to,
                        automatic = true,
                        reason = "test"
                    }),
                    OccurredAt = occurredAt
                };
        });
    }

    [Test]
    [Arguments(100)]
    [Arguments(3000)]
    [Timeout(300_000)]
    public async Task Awdp_penalties_enter_the_total_only_after_their_round_settles(
        int finishedSeconds,
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_penalty_settlement")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var firstProjectionAt = DateTimeOffset.Parse("2026-09-08T00:01:30Z");
            var startedAt = firstProjectionAt.AddSeconds(-90);
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(firstProjectionAt);
            var fixture = CreateFixture(GameMode.Awdp, 0, owner.Id, firstProjectionAt);
            fixture.Competition.StartAt = startedAt;
            fixture.Competition.ConfigurationJson = JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                60,
                new(100, 100, 2, ScoreDecayMode.Fixed),
                new(100, 100, 2, ScoreDecayMode.Fixed),
                FlagWrongPenalty: 7,
                RequireBreakBeforeFix: false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var settledPenalty = fixture.Facts[0];
            settledPenalty.OccurredAt = startedAt.AddSeconds(30);
            settledPenalty.UpdatedAt = settledPenalty.OccurredAt;
            settledPenalty.Result = GameplayFactResult.Wrong;
            var pendingPenalty = new GameplayFact
            {
                Id = Guid.CreateVersion7(startedAt.AddSeconds(70)),
                CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                TeamId = fixture.Team.Id,
                ActorUserId = owner.Id,
                Kind = GameplayFactKind.BreakAttempt,
                OccurredAt = startedAt.AddSeconds(70),
                Value = "flag{current-round-wrong}",
                ValueSha256 = new byte[32],
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Wrong,
                UpdatedAt = startedAt.AddSeconds(70)
            };

            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.AddRange(settledPenalty, pendingPenalty);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            var beforeSettlement = await cache.CreateBundleAsync(
                fixture.Competition.Id,
                firstProjectionAt,
                cancellationToken);
            var finishedAt = startedAt.AddSeconds(finishedSeconds);
            fixture.Competition.Status = CompetitionStatus.Finished;
            fixture.Competition.UpdatedAt = finishedAt;
            db.CompetitionEvents.AddRange(
                LifecycleEvent(
                    fixture.Competition.Id,
                    CompetitionStatus.Published,
                    CompetitionStatus.Running,
                    startedAt),
                LifecycleEvent(
                    fixture.Competition.Id,
                    CompetitionStatus.Running,
                    CompetitionStatus.Finished,
                    finishedAt));
            await db.SaveChangesAsync(cancellationToken);
            var afterSettlement = await cache.CreateBundleAsync(
                fixture.Competition.Id,
                finishedAt,
                cancellationToken);

            await Assert.That(beforeSettlement).IsNotNull();
            await Assert.That(beforeSettlement!.Scoreboard.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(-7);
            await Assert.That(afterSettlement).IsNotNull();
            await Assert.That(afterSettlement!.Scoreboard.Snapshot.Teams.Single().TotalScore)
                .IsEqualTo(-14);
            await Assert.That(afterSettlement.Scoreboard.Schema.Rounds[^1].EndAt)
                .IsEqualTo(finishedAt);
            var finishedRow = afterSettlement.Scoreboard.Snapshot.Teams.Single();
            await Assert.That(finishedRow.Slots.Count).IsEqualTo(2);
            await Assert.That(finishedRow.Slots.Sum(slot => slot.NetPoints ?? 0)).IsEqualTo(-14);
            await Assert.That(finishedRow.ScoreOutsideWindow).IsEqualTo(0);

            static CompetitionEvent LifecycleEvent(
                Guid competitionId,
                CompetitionStatus from,
                CompetitionStatus to,
                DateTimeOffset occurredAt) => new()
                {
                    Id = Guid.CreateVersion7(occurredAt),
                    CompetitionId = competitionId,
                    Kind = CompetitionEventKind.CompetitionLifecycleChanged,
                    Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Public,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        competitionStatus = to,
                        from,
                        to,
                        automatic = true,
                        reason = "test"
                    }),
                    OccurredAt = occurredAt
                };
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awd_long_history_uses_a_bounded_round_window_without_losing_total_score(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_bounded_round_window")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var projectedAt = DateTimeOffset.UtcNow;
            var startedAt = projectedAt.AddHours(-2);
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(projectedAt);
            var fixture = CreateFixture(GameMode.Awd, 0, owner.Id, projectedAt);
            fixture.Competition.StartAt = startedAt;
            fixture.Competition.ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default with { ServiceHealthyPoints = 100 },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var rounds = Enumerable.Range(1, 60)
                .Select(number => new ChallengeFlag
                {
                    Id = Guid.CreateVersion7(startedAt.AddMinutes(number)),
                    CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                    TeamId = fixture.Team.Id,
                    Flag = $"flag{{round-{number}}}",
                    FlagSha256 = new byte[32],
                    SpecificationKind = SpecificationKind.AwdRound,
                    SpecificationId = AwdRoundSpecificationId.FromRound(number).Value,
                    ValidStart = startedAt.AddMinutes(number - 1),
                    ValidUntil = startedAt.AddMinutes(number),
                    CreatedAt = startedAt.AddMinutes(number - 1)
                })
                .ToArray();

            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.ChallengeFlags.AddRange(rounds);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            var latest = await cache.CreateScoreboardAsync(
                fixture.Competition.Id,
                projectedAt,
                cancellationToken);
            await Assert.That(latest).IsNotNull();
            await Assert.That(latest!.Schema.Rounds.Select(round => round.Number))
                .IsEquivalentTo(Enumerable.Range(11, ScoreboardRoundWindow.DefaultSize));
            await Assert.That(latest.Schema.LatestRound).IsEqualTo(60);
            var latestTeam = latest.Snapshot.Teams.Single();
            await Assert.That(latestTeam.TotalScore).IsEqualTo(6_000);
            await Assert.That(latestTeam.ScoreOutsideWindow).IsEqualTo(1_000);
            await Assert.That(latestTeam.TotalScore).IsEqualTo(checked(
                latestTeam.ScoreOutsideWindow
                + latestTeam.Slots.Sum(slot => slot.NetPoints.GetValueOrDefault())
                + latestTeam.GlobalAdjustments.Sum(adjustment => adjustment.NetPoints)));

            var historical = await cache.CreateScoreboardWindowAsync(
                fixture.Competition.Id,
                50,
                projectedAt,
                cancellationToken);
            await Assert.That(historical).IsNotNull();
            await Assert.That(historical!.Schema.Rounds.Select(round => round.Number))
                .IsEquivalentTo(Enumerable.Range(1, ScoreboardRoundWindow.DefaultSize));
            var historicalTeam = historical.Snapshot.Teams.Single();
            await Assert.That(historicalTeam.TotalScore).IsEqualTo(6_000);
            await Assert.That(historicalTeam.ScoreOutsideWindow).IsEqualTo(1_000);

            var firstAttacker = CreateApprovedTeam(
                fixture.Competition.Id,
                owner.Id,
                "First attacker",
                projectedAt.AddMinutes(-10));
            var secondAttacker = CreateApprovedTeam(
                fixture.Competition.Id,
                owner.Id,
                "Second attacker",
                projectedAt.AddMinutes(-9));
            fixture.Competition.ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default with
                {
                    AttackRewardMode = AttackRewardMode.SplitVictimDefensePool,
                    VictimDefensePoolPoints = 100,
                    ServiceHealthyPoints = 0
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.Teams.AddRange(firstAttacker, secondAttacker);
            db.GameplayFacts.AddRange(
                CreateAttack(firstAttacker.Id, fixture.Team.Id, projectedAt.AddMinutes(-8)),
                CreateAttack(secondAttacker.Id, fixture.Team.Id, projectedAt.AddMinutes(-7)));
            await db.SaveChangesAsync(cancellationToken);

            var sharedPool = await cache.CreateScoreboardAsync(
                fixture.Competition.Id,
                projectedAt,
                cancellationToken);
            await Assert.That(sharedPool).IsNotNull();
            await Assert.That(sharedPool!.Snapshot.Teams
                    .Single(team => team.TeamId == fixture.Team.Id).TotalScore)
                .IsEqualTo(-100);
            await Assert.That(sharedPool.Snapshot.Teams
                    .Single(team => team.TeamId == firstAttacker.Id).TotalScore)
                .IsEqualTo(50);
            await Assert.That(sharedPool.Snapshot.Teams
                    .Single(team => team.TeamId == secondAttacker.Id).TotalScore)
                .IsEqualTo(50);

            Team CreateApprovedTeam(
                Guid competitionId,
                Guid captainId,
                string name,
                DateTimeOffset registeredAt) => new()
                {
                    Id = Guid.CreateVersion7(registeredAt),
                    CompetitionId = competitionId,
                    Name = name,
                    CaptainId = captainId,
                    MemberIds = [captainId],
                    InvitationToken = Guid.NewGuid().ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = registeredAt
                };

            GameplayFact CreateAttack(
                Guid attackerId,
                Guid victimId,
                DateTimeOffset occurredAt) => new()
                {
                    Id = Guid.CreateVersion7(occurredAt),
                    CompetitionId = fixture.Competition.Id,
                    CompetitionChallengeId = fixture.CompetitionChallenge.Id,
                    TeamId = attackerId,
                    ActorUserId = owner.Id,
                    VictimTeamId = victimId,
                    Kind = GameplayFactKind.FlagAttempt,
                    OccurredAt = occurredAt,
                    ReferenceKind = GameplayFactReferenceKind.AwdRound,
                    ReferenceId = rounds[0].SpecificationId,
                    Value = $"flag{{attack-{attackerId:N}}}",
                    ValueSha256 = new byte[32],
                    State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Correct,
                    UpdatedAt = occurredAt
                };
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Awdp_ban_and_unban_reproject_the_authoritative_cache(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_ban_reprojection")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(now);
            var fixture = CreateFixture(GameMode.Awdp, 0, owner.Id, now);
            var scoredFact = fixture.Facts[0];
            scoredFact.Result = GameplayFactResult.Correct;
            db.Users.Add(owner);
            db.Competitions.Add(fixture.Competition);
            db.Challenges.Add(fixture.Challenge);
            db.CompetitionChallenges.Add(fixture.CompetitionChallenge);
            db.Teams.Add(fixture.Team);
            db.GameplayFacts.Add(scoredFact);
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cacheProvider = cacheServices.GetRequiredService<IFusionCacheProvider>();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheProvider);
            await cache.RefreshAsync(fixture.Competition.Id, cancellationToken);
            var initial = await cache.GetScoreboardAsync(fixture.Competition.Id, cancellationToken);
            var initialTeam = initial!.Snapshot.Teams.Single(team => team.TeamId == fixture.Team.Id);
            await Assert.That(initialTeam.RankingState).IsEqualTo(ScoreboardRankingState.Eligible);
            await Assert.That(initialTeam.TotalScore).IsNotEqualTo(0L);

            var projectionKey = $"scoreboard:v1:{fixture.Competition.Id:N}";
            var namedCache = cacheProvider.GetCache(NoCtfCacheNames.Leaderboards);
            var cachedBundle = await namedCache.GetOrDefaultAsync<ScoreboardProjectionBundle?>(
                projectionKey,
                null,
                token: cancellationToken);
            var futureVersion = initial.Snapshot.Version + 1_000_000;
            await namedCache.SetAsync(
                projectionKey,
                cachedBundle! with
                {
                    Scoreboard = cachedBundle!.Scoreboard with
                    {
                        Snapshot = cachedBundle.Scoreboard.Snapshot with { Version = futureVersion }
                    }
                },
                token: cancellationToken);
            await cache.RefreshAsync(fixture.Competition.Id, cancellationToken);
            var monotonicRefresh = await cache.GetScoreboardAsync(
                fixture.Competition.Id,
                cancellationToken);
            await Assert.That(monotonicRefresh!.Snapshot.Version).IsGreaterThan(futureVersion);

            var outbox = new RecordingOutbox();
            var moderation = new TeamModerationStore(db, outbox);
            var ban = await moderation.ApplyAsync(new(
                fixture.Competition.Id,
                fixture.Team.Id,
                owner.Id,
                true,
                "integration regression",
                now.AddMinutes(1)), cancellationToken);
            await Assert.That(ban.Succeeded).IsTrue();
            await ProjectLeaderboardAsync(db, cache, cancellationToken);

            var banned = await cache.GetScoreboardAsync(fixture.Competition.Id, cancellationToken);
            var bannedTeam = banned!.Snapshot.Teams.Single(team => team.TeamId == fixture.Team.Id);
            await Assert.That(bannedTeam.RankingState).IsEqualTo(ScoreboardRankingState.Banned);
            await Assert.That(bannedTeam.TotalScore).IsEqualTo(0L);
            await Assert.That(bannedTeam.Slots).IsEmpty();

            var unban = await moderation.ApplyAsync(new(
                fixture.Competition.Id,
                fixture.Team.Id,
                owner.Id,
                false,
                null,
                now.AddMinutes(2)), cancellationToken);
            await Assert.That(unban.Succeeded).IsTrue();
            await ProjectLeaderboardAsync(db, cache, cancellationToken);

            var restored = await cache.GetScoreboardAsync(fixture.Competition.Id, cancellationToken);
            var restoredTeam = restored!.Snapshot.Teams.Single(team => team.TeamId == fixture.Team.Id);
            await Assert.That(restoredTeam.RankingState).IsEqualTo(ScoreboardRankingState.Eligible);
            await Assert.That(restoredTeam.TotalScore).IsEqualTo(initialTeam.TotalScore);
            await Assert.That(restoredTeam.Slots).IsNotEmpty();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Projection_aggregates_fact_history_in_PostgreSQL_for_every_mode(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_leaderboard_projection")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var commands = new CommandCaptureInterceptor();
            var transactions = new TransactionCaptureInterceptor();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(commands, transactions)
                .Options;
            var projectedAt = DateTimeOffset.UtcNow;

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var owner = CreateUser(projectedAt);
            db.Users.Add(owner);

            var fixtures = Enum.GetValues<GameMode>()
                .Select((mode, index) => CreateFixture(mode, index, owner.Id, projectedAt))
                .ToArray();
            foreach (var fixture in fixtures)
            {
                fixture.Competition.TracksEnabled = false;
                fixture.Competition.TrackConfigurationJson = CompetitionTrackConfiguration.Serialize(new(
                    CompetitionTrackConfiguration.CurrentSchemaVersion,
                    [
                        CompetitionTrackConfiguration.DefaultFor(fixture.Competition.Mode).DefaultTrack,
                        new CompetitionTrackDefinition(
                            "internal",
                            "Internal",
                            IsDefault: false,
                            IsPublicSelectable: false,
                            IsInternal: true,
                            EarnsScore: false,
                            EarnsBlood: false,
                            AffectsDynamicChallengeScore: false,
                            VisibleOnLeaderboard: false,
                            AffectsCompetitiveResults: false)
                    ]));
                fixture.Team.TrackKey = "internal";
            }
            db.Competitions.AddRange(fixtures.Select(fixture => fixture.Competition));
            db.Challenges.AddRange(fixtures.Select(fixture => fixture.Challenge));
            db.CompetitionChallenges.AddRange(fixtures.Select(fixture => fixture.CompetitionChallenge));
            db.Teams.AddRange(fixtures.Select(fixture => fixture.Team));
            db.ChallengeFlags.AddRange(fixtures.SelectMany(fixture => fixture.Flags));
            db.GameplayFacts.AddRange(fixtures.SelectMany(fixture => fixture.Facts));
            await db.SaveChangesAsync(cancellationToken);

            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.Leaderboards)
                .Services
                .BuildServiceProvider();
            var cache = new FusionLeaderboardCache(
                db,
                new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(),
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            transactions.Clear();
            var awdp = fixtures.Single(fixture => fixture.Competition.Mode == GameMode.Awdp);
            _ = await cache.CreateScoreboardWindowAsync(
                awdp.Competition.Id,
                1,
                projectedAt,
                cancellationToken);
            await Assert.That(transactions.IsolationLevels)
                .IsEquivalentTo([IsolationLevel.RepeatableRead]);

            foreach (var fixture in fixtures)
            {
                commands.Clear();
                var bundle = await cache.CreateBundleAsync(
                    fixture.Competition.Id,
                    projectedAt,
                    cancellationToken);

                await Assert.That(bundle).IsNotNull();
                await Assert.That(bundle!.Scoreboard.Snapshot.Teams).HasSingleItem();
                await Assert.That(bundle.Scoreboard.Snapshot.Teams[0].Slots).IsNotEmpty();
                await Assert.That(bundle.Scoreboard.Snapshot.TracksEnabled).IsFalse();
                await Assert.That(bundle.Scoreboard.Snapshot.Tracks).HasSingleItem();
                await Assert.That(bundle.Scoreboard.Snapshot.Tracks[0].Key)
                    .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
                await Assert.That(bundle.Scoreboard.Snapshot.Teams[0].TrackKey)
                    .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
                var expected = CreateExpectedScoreboard(fixture, projectedAt);
                var actualRow = bundle.Scoreboard.Snapshot.Teams[0];
                var expectedRow = expected.Snapshot.Teams.Single();
                await Assert.That(actualRow.TotalScore).IsEqualTo(expectedRow.TotalScore);
                await Assert.That(actualRow.Slots.Count).IsEqualTo(expectedRow.Slots.Count);
                var actualSlots = actualRow.Slots.OrderBy(slot => slot.ColumnIndex).ToArray();
                var expectedSlots = expectedRow.Slots.OrderBy(slot => slot.ColumnIndex).ToArray();
                for (var slotIndex = 0; slotIndex < expectedSlots.Length; slotIndex++)
                {
                    var actual = actualSlots[slotIndex];
                    var expectedSlot = expectedSlots[slotIndex];
                    await Assert.That(actual.ColumnIndex).IsEqualTo(expectedSlot.ColumnIndex);
                    await Assert.That(actual.ScoreState).IsEqualTo(expectedSlot.ScoreState);
                    await Assert.That(actual.EarnedPoints).IsEqualTo(expectedSlot.EarnedPoints);
                    await Assert.That(actual.DeductedPoints).IsEqualTo(expectedSlot.DeductedPoints);
                    await Assert.That(actual.NetPoints).IsEqualTo(expectedSlot.NetPoints);
                    await Assert.That(actual.EntryCount).IsEqualTo(expectedSlot.EntryCount);

                    var actualBreakdowns = actual.Breakdowns.OrderBy(item => item.Kind).ToArray();
                    var expectedBreakdowns = expectedSlot.Breakdowns.OrderBy(item => item.Kind).ToArray();
                    await Assert.That(actualBreakdowns).IsEquivalentTo(expectedBreakdowns);
                }
                var entryCount = bundle.Scoreboard.Snapshot.Teams[0].Slots.Sum(slot => slot.EntryCount);
                if (fixture.Competition.Mode == GameMode.Koh)
                    await Assert.That(entryCount).IsEqualTo(249);
                else
                    await Assert.That(entryCount).IsGreaterThanOrEqualTo(250);

                var factQueries = commands.Commands
                    .Where(command => command.Contains("gameplay_facts", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                await Assert.That(factQueries.Length).IsGreaterThanOrEqualTo(1);
                var maximumFactQueries = fixture.Competition.Mode == GameMode.Awd ? 5 : 2;
                await Assert.That(factQueries.Length).IsLessThanOrEqualTo(maximumFactQueries);
                await Assert.That(factQueries.Any(command =>
                        command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)))
                    .IsTrue();
            }

            var ctf = fixtures.Single(fixture => fixture.Competition.Mode == GameMode.Ctf);
            var detailReader = new ScoreboardDetailReader(db);
            var detailIds = new List<Guid>();
            DateTimeOffset? beforeOccurredAt = null;
            Guid? beforeId = null;
            while (true)
            {
                var page = await detailReader.ReadSlotAsync(new(
                    ctf.Competition.Id,
                    ctf.Team.Id,
                    ctf.CompetitionChallenge.Id,
                    GameMode.Ctf,
                    null,
                    null,
                    null,
                    projectedAt,
                    beforeOccurredAt,
                    beforeId,
                    73), cancellationToken);
                detailIds.AddRange(page.Select(fact => fact.Id));
                if (page.Count < 73)
                    break;
                beforeOccurredAt = page[^1].OccurredAt;
                beforeId = page[^1].Id;
            }
            await Assert.That(detailIds).Count().IsEqualTo(ctf.Facts.Count);
            await Assert.That(detailIds.Distinct()).Count().IsEqualTo(ctf.Facts.Count);
            await Assert.That(detailIds).IsEquivalentTo(ctf.Facts.Select(fact => fact.Id));

            var futureFact = new GameplayFact
            {
                Id = Guid.CreateVersion7(projectedAt.AddMinutes(1)),
                CompetitionId = ctf.Competition.Id,
                CompetitionChallengeId = ctf.CompetitionChallenge.Id,
                TeamId = ctf.Team.Id,
                ActorUserId = owner.Id,
                Kind = GameplayFactKind.FlagAttempt,
                OccurredAt = projectedAt.AddMinutes(1),
                Value = "flag{future}",
                ValueSha256 = new byte[32],
                State = GameplayFactState.Completed,
                Result = GameplayFactResult.Correct,
                UpdatedAt = projectedAt.AddMinutes(1)
            };
            db.GameplayFacts.Add(futureFact);
            await db.SaveChangesAsync(cancellationToken);
            var historical = await cache.CreateBundleAsync(
                ctf.Competition.Id,
                projectedAt,
                cancellationToken);

            await Assert.That(historical!.Scoreboard.EntryAllocations
                    .Select(item => item.Entry.Id))
                .DoesNotContain(futureFact.Id);
            await Assert.That(historical.Scoreboard.DetailActors
                    .Where(item => item.UserId == owner.Id)
                    .Select(item => item.DisplayName)
                    .Distinct())
                .IsEquivalentTo([owner.UserName]);

            var rejudgedFact = ctf.Facts.First(fact => fact.Result is not null);
            var originalState = rejudgedFact.State;
            var originalResult = rejudgedFact.Result;
            var originalFailureCode = rejudgedFact.FailureCode;
            var originalUpdatedAt = rejudgedFact.UpdatedAt;
            rejudgedFact.State = GameplayFactState.PlatformFailed;
            rejudgedFact.Result = null;
            rejudgedFact.FailureCode = GameplayFactFailureCode.CheckerPlatformError;
            rejudgedFact.UpdatedAt = projectedAt.AddMinutes(2);
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = Guid.CreateVersion7(projectedAt.AddTicks(-1)),
                CompetitionId = ctf.Competition.Id,
                Kind = CompetitionEventKind.GameplayFactAdjudicated,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Team,
                SubjectType = EntityReferenceKind.GameplayFact,
                SubjectId = rejudgedFact.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    gameplayFactState = originalState.ToString(),
                    gameplayFactResult = originalResult!.Value.ToString()
                }),
                OccurredAt = projectedAt.AddTicks(-1)
            });
            await db.SaveChangesAsync(cancellationToken);

            var restoredPage = await detailReader.ReadSlotAsync(new(
                ctf.Competition.Id,
                ctf.Team.Id,
                ctf.CompetitionChallenge.Id,
                GameMode.Ctf,
                null,
                null,
                null,
                projectedAt,
                null,
                null,
                500), cancellationToken);
            var restoredFact = restoredPage.Single(fact => fact.Id == rejudgedFact.Id);
            await Assert.That(restoredFact.State).IsEqualTo(originalState);
            await Assert.That(restoredFact.Result).IsEqualTo(originalResult);
            await Assert.That(restoredFact.ScoringIdentityKnown).IsFalse();

            rejudgedFact.State = originalState;
            rejudgedFact.Result = originalResult;
            rejudgedFact.FailureCode = originalFailureCode;
            rejudgedFact.UpdatedAt = originalUpdatedAt;
            await db.SaveChangesAsync(cancellationToken);

            await cache.RefreshAsync(ctf.Competition.Id, cancellationToken);
            await Assert.That(transactions.IsolationLevels)
                .Contains(IsolationLevel.RepeatableRead);
        });
    }

    private static ScoreboardProjection CreateExpectedScoreboard(Fixture fixture, DateTimeOffset projectedAt)
    {
        var team = fixture.Team;
        var challenge = fixture.CompetitionChallenge;
        var facts = fixture.Facts.Select(fact => new LeaderboardGameplayFact(
            fact.Id,
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.OccurredAt,
            fact.State,
            fact.Result,
            fact.FailureCode,
            fact.ReferenceKind,
            fact.ReferenceId,
            fact.VictimTeamId,
            null,
            fact.Value,
            null,
            fact.ActorUserId)).ToArray();
        var rounds = fixture.Flags.Select(flag => new LeaderboardAwdRoundFact(
            flag.CompetitionChallengeId!.Value,
            flag.TeamId!.Value,
            flag.SpecificationId!.Value,
            flag.ValidStart!.Value,
            flag.ValidUntil!.Value)).ToArray();
        return new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(new(
            fixture.Competition.Id,
            fixture.Competition.Mode,
            [new LeaderboardTeamFact(team.Id, team.Name, false, false, team.RegisteredAt)],
            facts,
            [new LeaderboardChallengeFact(
                challenge.Id,
                fixture.Challenge.Direction,
                fixture.Challenge.Title,
                false,
                challenge.RulesJson,
                challenge.Order,
                challenge.IsPublished)],
            fixture.Competition.ConfigurationJson,
            fixture.Competition.StartAt,
            AwdRounds: rounds,
            ProjectedAt: projectedAt,
            CompetitionStatus: fixture.Competition.Status));
    }

    private static Fixture CreateFixture(
        GameMode mode,
        int index,
        Guid ownerId,
        DateTimeOffset projectedAt)
    {
        var competitionId = Guid.CreateVersion7(projectedAt.AddMinutes(index));
        var challengeId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 10));
        var competitionChallengeId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 20));
        var teamId = Guid.CreateVersion7(projectedAt.AddMinutes(index + 30));
        var start = projectedAt.AddMinutes(-20);
        var rules = new GameModeChallengeConfigurationCatalog().GetDefaultJson(mode);
        var definition = new GameModeChallengeConfigurationCatalog().GetDefaultDefinitionJson(mode);
        var competition = new Competition
        {
            Id = competitionId,
            Title = $"{mode} projection",
            OwnerId = ownerId,
            Mode = mode,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(mode),
            FlagDerivationSecret = new byte[32],
            StartAt = start,
            EndAt = projectedAt.AddHours(1),
            Status = CompetitionStatus.Running,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            CreatedAt = start,
            UpdatedAt = start
        };
        var challenge = new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = mode,
            Visibility = ChallengeVisibility.Private,
            Title = $"{mode} challenge",
            Direction = "Pwn",
            DefinitionJson = definition,
            CreatedAt = start,
            UpdatedAt = start
        };
        var competitionChallenge = new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Order = 1,
            IsPublished = true,
            RulesJson = rules,
            UpdatedAt = start
        };
        var team = new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = $"{mode} team",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = $"{index:D2}".PadRight(32, 'a'),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = start
        };
        var kind = mode switch
        {
            GameMode.Ctf => GameplayFactKind.FlagAttempt,
            GameMode.Awd => GameplayFactKind.FlagAttempt,
            GameMode.Awdp => GameplayFactKind.BreakAttempt,
            GameMode.Koh => GameplayFactKind.KohControlObservation,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        var result = mode == GameMode.Koh
            ? GameplayFactResult.Controlled
            : GameplayFactResult.Wrong;
        var roundId = Guid.CreateVersion7(start);
        var facts = Enumerable.Range(0, 250)
            .Select(sequence => new GameplayFact
            {
                Id = Guid.CreateVersion7(start.AddSeconds(sequence + 1)),
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = mode == GameMode.Koh && sequence == 100 ? null : teamId,
                ActorUserId = mode == GameMode.Koh ? null : ownerId,
                Kind = kind,
                OccurredAt = start.AddSeconds(sequence + 1),
                ReferenceKind = mode == GameMode.Awd ? GameplayFactReferenceKind.AwdRound : null,
                ReferenceId = mode == GameMode.Awd ? roundId : null,
                Value = mode == GameMode.Koh ? null : $"flag{{{sequence:D8}}}",
                ValueSha256 = mode == GameMode.Koh ? null : new byte[32],
                State = GameplayFactState.Completed,
                Result = mode == GameMode.Koh && sequence == 100
                    ? GameplayFactResult.Uncontrolled
                    : result,
                UpdatedAt = start.AddSeconds(sequence + 1)
            })
            .ToArray();
        IReadOnlyList<ChallengeFlag> flags = mode == GameMode.Awd
            ?
            [
                new ChallengeFlag
                {
                    Id = Guid.CreateVersion7(start.AddMinutes(1)),
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    Flag = "flag{round}",
                    FlagSha256 = new byte[32],
                    SpecificationKind = SpecificationKind.AwdRound,
                    SpecificationId = roundId,
                    ValidStart = start,
                    ValidUntil = start.AddMinutes(5),
                    CreatedAt = start
                }
            ]
            : [];
        return new(competition, challenge, competitionChallenge, team, facts, flags);
    }

    private static User CreateUser(DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        UserName = "administrator",
        NormalizedUserName = "ADMINISTRATOR",
        Email = "administrator@example.test",
        PasswordHash = "test",
        Role = UserRole.Administrator,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static GameplayFact CreateManualAdjustment(
        Fixture fixture,
        Guid actorUserId,
        DateTimeOffset occurredAt,
        string value) => new()
        {
            Id = Guid.CreateVersion7(occurredAt),
            CompetitionId = fixture.Competition.Id,
            CompetitionChallengeId = fixture.CompetitionChallenge.Id,
            TeamId = fixture.Team.Id,
            ActorUserId = actorUserId,
            Kind = GameplayFactKind.ManualAdjustment,
            OccurredAt = occurredAt,
            Value = value,
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Applied,
            UpdatedAt = occurredAt
        };

    private static async Task ProjectLeaderboardAsync(
        NoCtfDbContext db,
        ILeaderboardCache cache,
        CancellationToken cancellationToken)
    {
        await new LeaderboardMessageHandler(cache).Handle(
            new ProjectLeaderboard(db.Competitions.Select(item => item.Id).Single()),
            cancellationToken);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<ProjectLeaderboard> ProjectLeaderboardMessages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            if (message is ProjectLeaderboard projection)
                ProjectLeaderboardMessages.Add(projection);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        Competition Competition,
        Challenge Challenge,
        CompetitionChallenge CompetitionChallenge,
        Team Team,
        IReadOnlyList<GameplayFact> Facts,
        IReadOnlyList<ChallengeFlag> Flags);

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> Commands => commands;

        public void Clear() => commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionCaptureInterceptor : DbTransactionInterceptor
    {
        private readonly List<IsolationLevel> isolationLevels = [];

        public IReadOnlyList<IsolationLevel> IsolationLevels => isolationLevels;

        public void Clear() => isolationLevels.Clear();

        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result)
        {
            isolationLevels.Add(result.IsolationLevel);
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            isolationLevels.Add(result.IsolationLevel);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        private bool failNextCommit;

        public void FailNextCommit() => failNextCommit = true;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!failNextCommit)
                return ValueTask.FromResult(result);
            failNextCommit = false;
            throw new InvalidOperationException("Injected leaderboard projection commit failure.");
        }
    }

    private sealed class LockObservingFailingPublicationFence(
        string connectionString,
        Guid competitionId) : ILeaderboardPublicationFence
    {
        public bool ProjectionLockWasAvailable { get; private set; }

        public Task<long> IssueAsync(
            Guid ignoredCompetitionId,
            long minimumFence,
            CancellationToken cancellationToken) => Task.FromResult(minimumFence);

        public async Task<bool> TryCommitAsync(
            Guid ignoredCompetitionId,
            long fence,
            string payload,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var claim = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(hashtextextended(@competition_id, 0))",
                connection);
            claim.Parameters.AddWithValue("competition_id", competitionId.ToString("N"));
            ProjectionLockWasAvailable = (bool)(await claim.ExecuteScalarAsync(cancellationToken))!;
            if (ProjectionLockWasAvailable)
            {
                await using var release = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(hashtextextended(@competition_id, 0))",
                    connection);
                release.Parameters.AddWithValue("competition_id", competitionId.ToString("N"));
                _ = await release.ExecuteScalarAsync(cancellationToken);
            }
            throw new InvalidOperationException("Injected leaderboard publication failure.");
        }

        public Task<LeaderboardFencedPayload?> GetAsync(
            Guid ignoredCompetitionId,
            CancellationToken cancellationToken) => Task.FromResult<LeaderboardFencedPayload?>(null);

        public Task<bool> IsCurrentAsync(
            Guid ignoredCompetitionId,
            long fence,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task InvalidateAsync(
            Guid ignoredCompetitionId,
            long minimumFence,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
