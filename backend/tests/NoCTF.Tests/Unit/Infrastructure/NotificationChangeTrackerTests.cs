using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class NotificationChangeTrackerTests
{
    [Test]
    public async Task Dependency_injection_attaches_the_notification_interceptors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var publisher = new RecordingPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INotificationChangePublisher>(publisher);
        services.AddDbContext<NoCtfDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Notifications.Add(Notification(new(NotificationTargetType.User, Guid.NewGuid())));

        await db.SaveChangesAsync();

        await Assert.That(publisher.Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Implicit_save_publishes_only_after_notification_is_persisted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var audience = new NotificationAudience(NotificationTargetType.User, Guid.NewGuid());
        fixture.Db.Notifications.Add(Notification(audience));

        await fixture.Db.SaveChangesAsync();

        await Assert.That(fixture.Publisher.Changes.Count).IsEqualTo(1);
        await Assert.That(fixture.Publisher.Changes[0].Audiences).IsEquivalentTo([audience]);
    }

    [Test]
    public async Task Explicit_transaction_publishes_on_commit_not_on_save()
    {
        await using var fixture = await Fixture.CreateAsync();
        var audience = new NotificationAudience(NotificationTargetType.TeamMembers, Guid.NewGuid());
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Notifications.Add(Notification(audience));
        await fixture.Db.SaveChangesAsync();

        await Assert.That(fixture.Publisher.Changes.Count).IsEqualTo(0);
        await transaction.CommitAsync();
        await Assert.That(fixture.Publisher.Changes.Count).IsEqualTo(1);
        await Assert.That(fixture.Publisher.Changes[0].Audiences).IsEquivalentTo([audience]);
    }

    [Test]
    public async Task Rolled_back_notification_does_not_publish()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        fixture.Db.Notifications.Add(Notification(new(NotificationTargetType.User, Guid.NewGuid())));
        await fixture.Db.SaveChangesAsync();

        await transaction.RollbackAsync();

        await Assert.That(fixture.Publisher.Changes.Count).IsEqualTo(0);
    }

    private static MessageNotification Notification(NotificationAudience audience) => new()
    {
        Id = Guid.NewGuid(),
        TargetType = audience.TargetType,
        TargetId = audience.TargetId,
        SourceType = NotificationSourceType.System,
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

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public NoCtfDbContext Db { get; }
        public RecordingPublisher Publisher { get; }

        private Fixture(SqliteConnection connection, NoCtfDbContext db, RecordingPublisher publisher)
        {
            this.connection = connection;
            Db = db;
            Publisher = publisher;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseSqlite(connection)
                .Options;
            var publisher = new RecordingPublisher();
            var db = new NoCtfDbContext(options, notificationPublisher: publisher);
            await db.Database.EnsureCreatedAsync();
            return new(connection, db, publisher);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
