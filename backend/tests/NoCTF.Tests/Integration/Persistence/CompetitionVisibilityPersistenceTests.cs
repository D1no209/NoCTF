using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionVisibilityPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Scheduled_freeze_is_revision_fenced_persisted_and_audited(
        CancellationToken ct)
    {
        await RunAsync("noctf_visibility_freeze", async fixture =>
        {
            var outbox = new RecordingOutbox();
            var snapshots = new RecordingSnapshotFactory();
            var startsAt = fixture.Now.AddMinutes(10);
            await using (var configureDb = new NoCtfDbContext(fixture.Options))
            {
                var store = new CompetitionVisibilityStore(configureDb, snapshots, outbox);
                var result = await store.UpdateAsync(new(
                    fixture.CompetitionId,
                    CompetitionLeaderboardVisibility.Frozen,
                    startsAt,
                    0,
                    fixture.HumanObserverId,
                    "freeze window",
                    fixture.Now), ct);

                await Assert.That(result.State)
                    .IsEqualTo(CompetitionVisibilityMutationState.Updated);
                await Assert.That(result.Configuration?.EffectiveVisibility)
                    .IsEqualTo(CompetitionLeaderboardVisibility.Normal);
                await Assert.That(outbox.Scheduled).HasSingleItem();
                await Assert.That(outbox.Scheduled[0].At).IsEqualTo(startsAt);
                await Assert.That(snapshots.Requests).IsEmpty();
            }

            await using (var applyDb = new NoCtfDbContext(fixture.Options))
            {
                var store = new CompetitionVisibilityStore(applyDb, snapshots, outbox);
                await store.ApplyScheduledAsync(
                    fixture.CompetitionId,
                    expectedRevision: 1,
                    startsAt.AddSeconds(1),
                    ct);
                await store.ApplyScheduledAsync(
                    fixture.CompetitionId,
                    expectedRevision: 1,
                    startsAt.AddSeconds(2),
                    ct);
            }

            await using var verify = new NoCtfDbContext(fixture.Options);
            var competition = await verify.Competitions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == fixture.CompetitionId, ct);
            var snapshot = JsonSerializer.Deserialize<LeaderboardResponse>(
                competition.FrozenLeaderboardSnapshotJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await Assert.That(competition.LeaderboardVisibility)
                .IsEqualTo(CompetitionLeaderboardVisibility.Frozen);
            await Assert.That(competition.LeaderboardVisibilityAppliedAt).IsNotNull();
            await Assert.That(snapshot?.DataAsOf).IsEqualTo(startsAt);
            await Assert.That(snapshot?.DataScope).IsEqualTo(LeaderboardDataScope.Frozen);
            await Assert.That(snapshots.Requests).HasSingleItem();
            await Assert.That(snapshots.Requests[0].ProjectedAt).IsEqualTo(startsAt);
            await Assert.That(await verify.Set<CompetitionLeaderboardVisibilityAudit>()
                .CountAsync(audit => audit.CompetitionId == fixture.CompetitionId, ct))
                .IsEqualTo(1);
            await Assert.That(outbox.Published.OfType<InvalidateLeaderboard>().Count())
                .IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Blackout_hides_public_and_bot_data_but_trusted_humans_remain_live(
        CancellationToken ct)
    {
        await RunAsync("noctf_visibility_access", async fixture =>
        {
            await using var db = new NoCtfDbContext(fixture.Options);
            var access = new CompetitionVisibilityAccess(db);

            var participant = await access.ResolveAsync(
                fixture.ParticipantId,
                fixture.CompetitionId,
                fixture.Now,
                ct);
            var observerBot = await access.ResolveAsync(
                fixture.ObserverBotId,
                fixture.CompetitionId,
                fixture.Now,
                ct);
            var humanObserver = await access.ResolveAsync(
                fixture.HumanObserverId,
                fixture.CompetitionId,
                fixture.Now,
                ct);

            await Assert.That(participant?.DataScope).IsEqualTo(LeaderboardDataScope.Hidden);
            await Assert.That(observerBot?.DataScope).IsEqualTo(LeaderboardDataScope.Hidden);
            await Assert.That(humanObserver?.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            await Assert.That(humanObserver?.Visibility)
                .IsEqualTo(CompetitionLeaderboardVisibility.Blackout);
        }, CompetitionLeaderboardVisibility.Blackout);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Finishing_competition_reveals_final_board_and_invalidates_schedule(
        CancellationToken ct)
    {
        await RunAsync("noctf_visibility_finish", async fixture =>
        {
            var outbox = new RecordingOutbox();
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                var applied = await new CompetitionLifecycleStore(db, null!, outbox)
                    .TryTransitionWithAuditAsync(
                        fixture.CompetitionId,
                        CompetitionStatus.Running,
                        CompetitionStatus.Finished,
                        fixture.HumanObserverId,
                        "manual finish",
                        false,
                        CompetitionLifecycleEffects.CleanupRuntimes,
                        ct);
                await Assert.That(applied).IsTrue();
            }

            await using var verify = new NoCtfDbContext(fixture.Options);
            var competition = await verify.Competitions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == fixture.CompetitionId, ct);
            var audit = await verify.Set<CompetitionLeaderboardVisibilityAudit>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.CompetitionId == fixture.CompetitionId, ct);
            await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Finished);
            await Assert.That(competition.LeaderboardVisibility)
                .IsEqualTo(CompetitionLeaderboardVisibility.Normal);
            await Assert.That(competition.LeaderboardVisibilityStartsAt).IsNull();
            await Assert.That(competition.FrozenLeaderboardSnapshotJson).IsNull();
            await Assert.That(competition.LeaderboardVisibilityRevision).IsEqualTo(1);
            await Assert.That(audit.From).IsEqualTo(CompetitionLeaderboardVisibility.Frozen);
            await Assert.That(audit.To).IsEqualTo(CompetitionLeaderboardVisibility.Normal);
            await Assert.That(audit.Automatic).IsTrue();
        }, CompetitionLeaderboardVisibility.Frozen);
    }

    private static async Task RunAsync(
        string databaseName,
        Func<Fixture, Task> test,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase(databaseName)
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, visibility);
            await test(fixture);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CompetitionLeaderboardVisibility visibility)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync();
        var rawNow = DateTimeOffset.UtcNow;
        var now = rawNow.AddTicks(-(rawNow.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7(now);
        var humanObserverId = Guid.CreateVersion7(now.AddMilliseconds(1));
        var observerBotId = Guid.CreateVersion7(now.AddMilliseconds(2));
        var participantId = Guid.CreateVersion7(now.AddMilliseconds(3));
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(4));
        db.Users.AddRange(
            User(ownerId, "visibility-owner", UserKind.Human),
            User(humanObserverId, "visibility-observer", UserKind.Human),
            User(observerBotId, "visibility-bot", UserKind.Bot),
            User(participantId, "visibility-participant", UserKind.Human));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Visibility",
            OwnerId = ownerId,
            ObserverIds = [humanObserverId, observerBotId],
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            LeaderboardVisibility = visibility,
            LeaderboardVisibilityStartsAt = visibility == CompetitionLeaderboardVisibility.Normal
                ? null
                : now.AddMinutes(-1),
            LeaderboardVisibilityAppliedAt = visibility == CompetitionLeaderboardVisibility.Normal
                ? null
                : now.AddMinutes(-1),
            FrozenLeaderboardSnapshotJson = visibility == CompetitionLeaderboardVisibility.Frozen
                ? JsonSerializer.Serialize(new LeaderboardResponse(
                    competitionId,
                    now.AddMinutes(-1),
                    [])
                {
                    Visibility = CompetitionLeaderboardVisibility.Frozen,
                    DataScope = LeaderboardDataScope.Frozen,
                    DataAsOf = now.AddMinutes(-1)
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-30),
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return new(
            options,
            now,
            competitionId,
            humanObserverId,
            observerBotId,
            participantId);
    }

    private static User User(Guid id, string name, UserKind kind) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name}@EXAMPLE.TEST",
        PasswordHash = "test",
        Kind = kind,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed record Fixture(
        DbContextOptions<NoCtfDbContext> Options,
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid HumanObserverId,
        Guid ObserverBotId,
        Guid ParticipantId);

    private sealed class RecordingSnapshotFactory : ILeaderboardSnapshotFactory
    {
        public List<(Guid CompetitionId, DateTimeOffset ProjectedAt, bool Historical)> Requests { get; } = [];

        public Task<LeaderboardResponse?> CreateAsync(
            Guid competitionId,
            DateTimeOffset projectedAt,
            bool historical,
            CancellationToken cancellationToken)
        {
            Requests.Add((competitionId, projectedAt, historical));
            return Task.FromResult<LeaderboardResponse?>(new(
                competitionId,
                projectedAt,
                []));
        }
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            throw new NotSupportedException();
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
