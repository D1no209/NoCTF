using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionVisibility")]
public sealed class CompetitionVisibilityPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Visibility_schedule_is_stored_as_two_timestamps_and_audited(
        CancellationToken ct)
    {
        await RunAsync("noctf_visibility_schedule", async fixture =>
        {
            var frozenAt = fixture.Now.AddMinutes(10);
            var hiddenAt = fixture.Now.AddMinutes(20);
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                var outbox = new RecordingOutbox();
                var store = new CompetitionVisibilityStore(
                    db,
                    new CompetitionEventStore(db, outbox));
                var result = await store.UpdateAsync(new(
                    fixture.CompetitionId,
                    frozenAt,
                    hiddenAt,
                    fixture.HumanObserverId,
                    "broadcast windows",
                    fixture.Now), ct);

                await Assert.That(result.State)
                    .IsEqualTo(CompetitionVisibilityMutationState.Updated);
                await Assert.That(result.Configuration?.EffectiveVisibility)
                    .IsEqualTo(CompetitionLeaderboardVisibility.Normal);
                await Assert.That(outbox.Scheduled).IsEmpty();
            }

            await using var verify = new NoCtfDbContext(fixture.Options);
            var competition = await verify.Competitions.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == fixture.CompetitionId, ct);
            await Assert.That(competition.FrozenStartAt).IsEqualTo(frozenAt);
            await Assert.That(competition.HiddenStartAt).IsEqualTo(hiddenAt);
            var audit = await verify.CompetitionEvents.AsNoTracking()
                .SingleAsync(@event => @event.CompetitionId == fixture.CompetitionId
                    && @event.Kind == CompetitionEventKind.LeaderboardVisibilityChanged, ct);
            using var payload = JsonDocument.Parse(audit.PayloadJson);
            await Assert.That(payload.RootElement.GetProperty("frozenStartAt").GetDateTimeOffset())
                .IsEqualTo(frozenAt);
            await Assert.That(payload.RootElement.GetProperty("hiddenStartAt").GetDateTimeOffset())
                .IsEqualTo(hiddenAt);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Blackout_hides_public_data_but_all_trusted_collaborators_remain_live(
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
            await Assert.That(observerBot?.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            await Assert.That(humanObserver?.DataScope).IsEqualTo(LeaderboardDataScope.Live);
            await Assert.That(humanObserver?.Visibility)
                .IsEqualTo(CompetitionLeaderboardVisibility.Blackout);
        }, CompetitionLeaderboardVisibility.Blackout);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Finishing_competition_clears_both_visibility_timestamps(
        CancellationToken ct)
    {
        await RunAsync("noctf_visibility_finish", async fixture =>
        {
            var outbox = new RecordingOutbox();
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                var applied = await new CompetitionLifecycleStore(
                        db,
                        null!,
                        outbox,
                        new CompetitionEventStore(db, outbox))
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
            await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Finished);
            await Assert.That(competition.FrozenStartAt).IsNull();
            await Assert.That(competition.HiddenStartAt).IsNull();
            await Assert.That(await verify.CompetitionEvents.AsNoTracking()
                .CountAsync(candidate => candidate.CompetitionId == fixture.CompetitionId
                    && candidate.Kind == CompetitionEventKind.LeaderboardVisibilityChanged, ct))
                .IsEqualTo(1);
        }, CompetitionLeaderboardVisibility.Frozen);
    }

    private static async Task RunAsync(
        string databaseName,
        Func<Fixture, Task> test,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
        await db.Database.EnsureCreatedAsync();
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
            FrozenStartAt = visibility == CompetitionLeaderboardVisibility.Frozen
                ? now.AddMinutes(-1)
                : null,
            HiddenStartAt = visibility == CompetitionLeaderboardVisibility.Blackout
                ? now.AddMinutes(-1)
                : null,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
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
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
