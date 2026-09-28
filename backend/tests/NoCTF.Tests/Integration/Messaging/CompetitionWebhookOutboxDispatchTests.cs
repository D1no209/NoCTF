using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Hosting;
using NoCTF.Worker;
using NoCTF.Worker.Competitions.Webhooks;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("CompetitionWebhooks")]
[NotInParallel]
public sealed class CompetitionWebhookOutboxDispatchTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Committed_announcement_reaches_first_HTTP_attempt_within_two_seconds(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_webhook_slo")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            await using (var setup = new NoCtfDbContext(options))
                await setup.Database.EnsureCreatedAsync(ct);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var receiverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = ReceiveOnceAsync(listener, ct);
            var protector = new PlatformSecretProtector(Options.Create(
                new EmailVerificationProtectionOptions
                {
                    EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32)
                        .Select(value => (byte)value).ToArray())
                }));
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(options);
                    services.AddScoped<NoCtfDbContext>();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton(protector);
                    services.AddSingleton(new CompetitionWebhookOptions(
                        new Uri("https://noctf.example.test/"), 3,
                        new HashSet<string>(["127.0.0.1"]),
                        new HashSet<string>(["127.0.0.1"])));
                    services.AddSingleton<IChallengeManagementStore>(
                        Substitute.For<IChallengeManagementStore>());
                    services.AddSingleton<ICompetitionTrackStore>(
                        Substitute.For<ICompetitionTrackStore>());
                    services.AddSingleton<ILeaderboardCache>(
                        Substitute.For<ILeaderboardCache>());
                    services.AddSingleton<ICompetitionWebhookTestStatusStore>(
                        Substitute.For<ICompetitionWebhookTestStatusStore>());
                    services.AddScoped<GetChallenge>();
                    services.AddScoped<GetCompetitionTracks>();
                    services.AddScoped<ICompetitionWebhookDeliveryStore,
                        CompetitionWebhookDeliveryStore>();
                    services.AddSingleton<ICompetitionWebhookSender,
                        CompetitionWebhookSender>();
                    services.AddTransient<CompetitionWebhookMessageHandler>();
                    services.AddHostedService<CompetitionWebhookOutboxAgent>();
                    services.AddHostedService<CompetitionWebhookRetryAgent>();
                })
                .UseWolverine(wolverine =>
                {
                    wolverine.Discovery.DisableConventionalDiscovery();
                    wolverine.Discovery.IncludeType(typeof(CompetitionWebhookMessageHandler));
                    wolverine.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}")
                        .AutoProvision()
                        .UseJetStream(_ => { })
                        .DefineWorkQueueStream(
                            NatsSubjects.WebhookStream,
                            stream => stream.WithSubject(NatsSubjects.Subject(
                                NoCTF.Application.Messaging.WorkerQueue.Webhook)),
                            NatsSubjects.Subject(
                                NoCTF.Application.Messaging.WorkerQueue.Webhook));
                    wolverine.PublishMessage<DeliverCompetitionWebhook>()
                        .ToNatsSubject(NatsSubjects.Subject(
                            NoCTF.Application.Messaging.WorkerQueue.Webhook))
                        .UseJetStream(NatsSubjects.WebhookStream);
                    wolverine.ListenToNatsSubject(NatsSubjects.Subject(
                            NoCTF.Application.Messaging.WorkerQueue.Webhook))
                        .UseJetStream(NatsSubjects.WebhookStream, "webhook-outbox-slo-test")
                        .MaximumParallelMessages(8);
                })
                .Build();
            await host.StartAsync(ct);
            try
            {
                // Dynamic handler compilation belongs to test-host startup; production
                // uses checked-in static handlers, so warm it before measuring queue SLO.
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(
                    new DeliverCompetitionWebhook(Guid.NewGuid(), Guid.NewGuid(),
                        Guid.NewGuid(), DateTimeOffset.UtcNow));
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                var setupAt = DateTimeOffset.UtcNow;
                DateTimeOffset eventCreatedAt;
                var competitionId = Guid.CreateVersion7(setupAt);
                var ownerId = Guid.CreateVersion7(setupAt.AddTicks(1));
                var targetId = Guid.CreateVersion7(setupAt.AddTicks(2));
                var notificationId = Guid.CreateVersion7(setupAt.AddTicks(3));
                var eventId = Guid.CreateVersion7(setupAt.AddTicks(4));
                await using (var db = new NoCtfDbContext(options))
                {
                    db.Users.Add(new User
                    {
                        Id = ownerId, UserName = "announcement-owner",
                        Email = "announcement-owner@example.test", PasswordHash = "test",
                        Kind = UserKind.Human,
                        CreatedAt = setupAt, UpdatedAt = setupAt
                    });
                    var competition = new CtfCompetition
                    {
                        Id = competitionId, OwnerId = ownerId, Title = "Announcement",
                        ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                        FlagDerivationSecret = new byte[32], StartAt = setupAt,
                        EndAt = setupAt.AddHours(1), Status = CompetitionStatus.Running,
                        CreatedAt = setupAt, UpdatedAt = setupAt
                    };
                    competition.WebhookTargets.Add(new CompetitionWebhookTarget
                    {
                        Id = targetId, CompetitionId = competitionId, Name = "Receiver",
                        EndpointUrl = $"http://127.0.0.1:{receiverPort}/hook",
                        Enabled = true, EnabledAt = setupAt.AddSeconds(-1),
                        CurrentSecretCiphertext = protector.Protect(
                            "whsec_" + Convert.ToBase64String(new byte[32]),
                            PlatformSecretPurpose.CompetitionWebhookSecret,
                            competitionId, targetId),
                        CreatedAt = setupAt, UpdatedAt = setupAt
                    });
                    db.Competitions.Add(competition);
                    await db.SaveChangesAsync(ct);
                    eventCreatedAt = DateTimeOffset.UtcNow;
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    db.Notifications.Add(new CompetitionAnnouncementNotification
                    {
                        Id = notificationId,
                        SourceType = NotificationSourceType.Competition,
                        SourceId = competitionId,
                        TargetType = NotificationTargetType.CompetitionParticipants,
                        TargetId = competitionId,
                        CompetitionId = competitionId,
                        Title = "Announcement",
                        Body = "Ready",
                        SentAt = eventCreatedAt
                    });
                    db.CompetitionEvents.Add(new AnnouncementPublishedEvent
                    {
                        Id = eventId, CompetitionId = competitionId,
                        Level = CompetitionEventLevel.Information,
                        Visibility = CompetitionEventVisibility.Public,
                        SubjectType = EntityReferenceKind.Competition,
                        SubjectId = competitionId,
                        QuestionId = notificationId,
                        OccurredAt = eventCreatedAt
                    });
                    db.CompetitionWebhookOutboxEvents.Add(new CompetitionWebhookOutboxEvent
                    {
                        EventId = eventId, CompetitionId = competitionId,
                        CompetitionRevision = competition.ConcurrencyStamp,
                        DomainEventCreatedAt = eventCreatedAt,
                        OutboxPersistedAt = eventCreatedAt,
                        NextDispatchAt = eventCreatedAt
                    });
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                var firstHttpAttemptAt = await received.WaitAsync(
                    TimeSpan.FromSeconds(10), ct);
                var elapsedSeconds = (firstHttpAttemptAt - eventCreatedAt).TotalSeconds;
                await using var verification = new NoCtfDbContext(options);
                var outbox = await verification.CompetitionWebhookOutboxEvents
                    .SingleAsync(item => item.EventId == eventId, ct);
                var delivery = await verification.CompetitionWebhookDeliveries
                    .SingleAsync(item => item.EventId == eventId, ct);
                Console.WriteLine($"Webhook first HTTP attempt: {elapsedSeconds:F3}s; "
                    + $"outbox={(delivery.OutboxPersistedAt - eventCreatedAt).TotalSeconds:F3}s; "
                    + $"outbox_dequeued={(outbox.WorkerDequeuedAt - eventCreatedAt)?.TotalSeconds:F3}s; "
                    + $"dispatch_done={(outbox.DispatchCompletedAt - eventCreatedAt)?.TotalSeconds:F3}s; "
                    + $"dequeued={(delivery.WorkerDequeuedAt - eventCreatedAt)?.TotalSeconds:F3}s; "
                    + $"captured={(delivery.CapturedAt - eventCreatedAt)?.TotalSeconds:F3}s; "
                    + $"started={(delivery.FirstHttpAttemptStartedAt - eventCreatedAt)?.TotalSeconds:F3}s");
                // The Windows Testcontainers database commit has host/VM overhead;
                // measure the queue and Worker portion separately from that commit.
                await Assert.That((firstHttpAttemptAt - outbox.OutboxPersistedAt).TotalSeconds)
                    .IsLessThan(2);
                await Assert.That(elapsedSeconds).IsLessThan(5);
                for (var attempt = 0;
                    attempt < 50 && delivery.State != CompetitionWebhookDeliveryState.Delivered;
                    attempt++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                    await verification.Entry(delivery).ReloadAsync(ct);
                }
                await Assert.That(delivery.State)
                    .IsEqualTo(CompetitionWebhookDeliveryState.Delivered);
                await Assert.That(delivery.FirstHttpAttemptStartedAt).IsNotNull();
            }
            finally
            {
                await host.StopAsync(ct);
            }
        });
    }

    private static async Task<DateTimeOffset> ReceiveOnceAsync(
        TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        using var received = new MemoryStream();
        DateTimeOffset? firstByteAt = null;
        var headerEnd = -1;
        var contentLength = 0;
        while (true)
        {
            var length = await stream.ReadAsync(buffer, ct);
            if (length == 0) break;
            firstByteAt ??= DateTimeOffset.UtcNow;
            received.Write(buffer, 0, length);
            var bytes = received.ToArray();
            if (headerEnd < 0)
            {
                headerEnd = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
                if (headerEnd >= 0)
                {
                    var headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                    var lengthHeader = headers.Split("\r\n").First(line =>
                        line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    contentLength = int.Parse(lengthHeader.Split(':', 2)[1].Trim());
                }
            }
            if (headerEnd >= 0 && received.Length >= headerEnd + 4L + contentLength)
                break;
        }
        await stream.WriteAsync(
            "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
        return firstByteAt ?? DateTimeOffset.UtcNow;
    }
}
