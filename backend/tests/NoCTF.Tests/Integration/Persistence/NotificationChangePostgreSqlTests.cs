using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), NotInParallel]
public sealed class NotificationChangePostgreSqlTests
{
    [Test, Timeout(300_000)]
    public async Task Notification_signal_follows_the_real_transaction_commit(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var publisher = new RecordingPublisher();
            await using var db = new NoCtfDbContext(options,
                notificationPublisher: publisher);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                db.Notifications.Add(Notification(Guid.NewGuid()));
                await db.SaveChangesAsync(cancellationToken);
                await Assert.That(publisher.Changes.Count).IsEqualTo(0);
                await transaction.CommitAsync(cancellationToken);
            }
            await Assert.That(publisher.Changes.Count).IsEqualTo(1);

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                db.Notifications.Add(Notification(Guid.NewGuid()));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
            }
            await Assert.That(publisher.Changes.Count).IsEqualTo(1);
            await Assert.That(await db.Notifications.CountAsync(cancellationToken)).IsEqualTo(1);
        });
    }

    private static MessageNotification Notification(Guid targetId) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = NotificationSourceType.System,
        TargetType = NotificationTargetType.User,
        TargetId = targetId,
        SentAt = DateTimeOffset.UtcNow
    };

    private sealed class RecordingPublisher : INotificationChangePublisher
    {
        public List<NotificationChanged> Changes { get; } = [];

        public Task PublishAsync(NotificationChanged change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }
}
