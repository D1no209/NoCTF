using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionWebhooks")]
public sealed class CompetitionWebhookOutboxPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Projection_and_http_retries_are_per_target_and_respect_retry_after(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_webhook_retries")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var competitionId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var firstTarget = Guid.NewGuid();
            var secondTarget = Guid.NewGuid();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            foreach (var targetId in new[] { firstTarget, secondTarget })
                db.CompetitionWebhookDeliveries.Add(new CompetitionWebhookDeliveryRecord
                {
                    EventId = eventId, TargetId = targetId,
                    CompetitionId = competitionId,
                    State = CompetitionWebhookDeliveryState.Pending,
                    PayloadState = CompetitionWebhookPayloadState.Unknown,
                    DomainEventCreatedAt = now,
                    OutboxPersistedAt = now,
                    CreatedAt = now,
                    NextRetryAt = now
                });
            await db.SaveChangesAsync(ct);

            var store = NewDeliveryStore(db);
            var delivery = new DeliverCompetitionWebhook(
                competitionId, eventId, firstTarget, now);
            await store.RecordProjectionWaitAsync(delivery,
                CompetitionWebhookProjectionFailure.MissingPublicTeam, now, ct);
            var first = await db.CompetitionWebhookDeliveries.SingleAsync(
                item => item.EventId == eventId && item.TargetId == firstTarget, ct);
            await Assert.That(first.NextRetryAt).IsEqualTo(now.AddMilliseconds(100));
            await Assert.That(first.PayloadState)
                .IsEqualTo(CompetitionWebhookPayloadState.ProjectionNotReady);
            await store.RecordHttpStartedAsync(delivery, now.AddMilliseconds(100), ct);
            await store.RecordHttpFailureAsync(delivery, 429, now.AddMinutes(1),
                permanent: false, now.AddMilliseconds(200), ct);
            await Assert.That(first.NextRetryAt).IsEqualTo(now.AddMinutes(1));
            await Assert.That(first.HttpRetryCount).IsEqualTo(1);

            for (var attempt = 0; attempt < 6; attempt++)
                await store.RecordProjectionWaitAsync(delivery,
                    CompetitionWebhookProjectionFailure.MissingPublicTeam,
                    now.AddSeconds(attempt + 1), ct);
            await Assert.That(first.State)
                .IsEqualTo(CompetitionWebhookDeliveryState.DeadLetter);
            await Assert.That(first.DeadLetterReason)
                .IsEqualTo(CompetitionWebhookDeadLetterReason.ProjectionUnavailable);
            var sibling = await db.CompetitionWebhookDeliveries.SingleAsync(
                item => item.EventId == eventId && item.TargetId == secondTarget, ct);
            await Assert.That(sibling.State)
                .IsEqualTo(CompetitionWebhookDeliveryState.Pending);
            await Assert.That(sibling.ProjectionRetryCount).IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Event_and_outbox_commit_atomically_and_recover_after_worker_restart(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_webhook_outbox")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(
                    typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName))
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var competitionId = Guid.CreateVersion7(now);
            var ownerId = Guid.CreateVersion7(now.AddTicks(1));
            var targetId = Guid.CreateVersion7(now.AddTicks(2));
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.Add(new User
                {
                    Id = ownerId, UserName = "webhook-owner",
                    Email = "webhook-owner@example.test", PasswordHash = "test",
                    Kind = UserKind.Human, CreatedAt = now, UpdatedAt = now
                });
                var competition = new CtfCompetition
                {
                    Id = competitionId, OwnerId = ownerId, Title = "Outbox",
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    FlagDerivationSecret = new byte[32], StartAt = now,
                    EndAt = now.AddHours(1), Status = CompetitionStatus.Running,
                    CreatedAt = now, UpdatedAt = now
                };
                competition.WebhookTargets.Add(new CompetitionWebhookTarget
                {
                    Id = targetId, CompetitionId = competitionId, Name = "Receiver",
                    EndpointUrl = "https://hooks.example.test/noctf",
                    Enabled = true, EnabledAt = now,
                    CurrentSecretCiphertext = [1, 2, 3],
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Competitions.Add(competition);
                await setup.SaveChangesAsync(ct);
            }

            var draft = new CompetitionEventDraft(
                competitionId, CompetitionEventKind.CompetitionLifecycleChanged,
                CompetitionEventLevel.Information, CompetitionEventVisibility.Public,
                now.AddSeconds(1),
                CompetitionStatus: CompetitionStatus.Running,
                PreviousCompetitionStatus: CompetitionStatus.Published);
            await using (var rollback = new NoCtfDbContext(options))
            {
                await using var transaction = await rollback.Database.BeginTransactionAsync(ct);
                var recorder = new CompetitionEventStore(
                    rollback, new NoOpPostCommitMessagePublisher());
                _ = await recorder.RecordAsync(draft, ct);
                await rollback.SaveChangesAsync(ct);
                await Assert.That(await rollback.CompetitionWebhookOutboxEvents.CountAsync(ct))
                    .IsEqualTo(1);
                await transaction.RollbackAsync(ct);
            }
            await using (var checkRollback = new NoCtfDbContext(options))
            {
                await Assert.That(await checkRollback.CompetitionWebhookOutboxEvents.CountAsync(ct))
                    .IsEqualTo(0);
                await Assert.That(await checkRollback.CompetitionEvents.CountAsync(ct))
                    .IsEqualTo(0);
            }

            Guid eventId;
            await using (var commit = new NoCtfDbContext(options))
            {
                await using var transaction = await commit.Database.BeginTransactionAsync(ct);
                var recorder = new CompetitionEventStore(
                    commit, new NoOpPostCommitMessagePublisher());
                eventId = await recorder.RecordAsync(draft, ct);
                await commit.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            await using (var restartedWorker = new NoCtfDbContext(options))
            {
                var outbox = await restartedWorker.CompetitionWebhookOutboxEvents
                    .AsNoTracking().SingleAsync(ct);
                await Assert.That(outbox.EventId).IsEqualTo(eventId);
                await Assert.That(outbox.Sequence).IsGreaterThan(0);
                await Assert.That(outbox.CompetitionRevision).IsNotEqualTo(Guid.Empty);
                var deliveries = NewDeliveryStore(restartedWorker);
                var claimed = await deliveries.ClaimPendingOutboxAsync(
                    now.AddMinutes(1), 10, ct);
                await Assert.That(claimed).HasSingleItem();
                var batch = await deliveries.PrepareBatchAsync(
                    new(competitionId, eventId), 10, ct);
                await Assert.That(batch.Deliveries).HasSingleItem();
                await Assert.That(await restartedWorker.CompetitionWebhookDeliveries.CountAsync(ct))
                    .IsEqualTo(1);
                await deliveries.MarkDispatchCompletedAsync(eventId, now.AddMinutes(1), ct);
            }
            await using (var secondWorker = new NoCtfDbContext(options))
            {
                var deliveries = NewDeliveryStore(secondWorker);
                await Assert.That(await deliveries.ClaimPendingOutboxAsync(
                    now.AddMinutes(2), 10, ct)).IsEmpty();
                await Assert.That(await secondWorker.CompetitionWebhookDeliveries.CountAsync(ct))
                    .IsEqualTo(1);
            }
        });
    }

    private static CompetitionWebhookDeliveryStore NewDeliveryStore(NoCtfDbContext db)
    {
        var protector = new PlatformSecretProtector(Options.Create(
            new EmailVerificationProtectionOptions
            {
                EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32)
                    .Select(value => (byte)value).ToArray())
            }));
        return new(db, protector,
            new GetChallenge(Substitute.For<IChallengeManagementStore>()),
            new GetCompetitionTracks(Substitute.For<ICompetitionTrackStore>()),
            Substitute.For<ILeaderboardCache>(),
            new CompetitionWebhookOptions(new Uri("https://noctf.example.test/"),
                10, new HashSet<string>(), new HashSet<string>()),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                CompetitionWebhookDeliveryStore>.Instance);
    }
}
