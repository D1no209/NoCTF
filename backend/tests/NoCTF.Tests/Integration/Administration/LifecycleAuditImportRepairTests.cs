using Microsoft.EntityFrameworkCore;
using NoCTF.CurrentImport;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class LifecycleAuditImportRepairTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Imported_audits_are_repaired_atomically_without_changing_their_history(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_lifecycle_import_repair").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var id = Guid.CreateVersion7();
            var userId = Guid.CreateVersion7();
            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                seed.Notifications.Add(new UserAccountLifecycleChangedNotification
                {
                    Id = id, TargetType = NotificationTargetType.PlatformAdministrators,
                    TargetId = Notification.PlatformAdministratorsTargetId,
                    UserId = userId, SourceId = userId, SourceType = NotificationSourceType.User,
                    ActionValue = (int)UserAccountLifecycleAction.EmailVerified,
                    Reason = "original reason", SentAt = now
                });
                await seed.SaveChangesAsync(ct);
            }
            await using var db = new NoCtfDbContext(options);
            var store = new PlatformAuditLogStore(db);
            await Assert.That(async () => { await store.QueryAsync(new(null, null, null, null, null, null, null, 100), ct); })
                .Throws<InvalidOperationException>();
            await Assert.That(() => UserLifecycleAuditRepair.RunAsync(db, 2, ct)).Throws<InvalidOperationException>();
            await Assert.That(await db.Notifications.AsNoTracking().Select(row => row.UserLifecycleAction).SingleAsync(ct)).IsNull();
            await Assert.That(await UserLifecycleAuditRepair.RunAsync(db, 1, ct)).IsEqualTo(1);
            await Assert.That(await UserLifecycleAuditRepair.RunAsync(db, 0, ct)).IsEqualTo(0);
            var audits = await store.QueryAsync(new(null, null, null, null, null, null, null, 100), ct);
            var audit = audits.Single();
            await Assert.That(audit.Id).IsEqualTo(id);
            await Assert.That(audit.UserAccountAction).IsEqualTo(UserAccountLifecycleAction.EmailVerified);
            await Assert.That(audit.Reason).IsEqualTo("original reason");
            await Assert.That(audit.OccurredAt).IsEqualTo(now);
            await Assert.That(audit.ActorId).IsEqualTo(userId);
            var retained = await db.Notifications.AsNoTracking().SingleAsync(ct);
            await Assert.That(retained.ActionValue).IsNull();
        });
    }

    [Test]
    [Timeout(120_000)]
    public async Task Unreadable_import_prevents_every_update(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_invalid_lifecycle_import").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            foreach (var action in new[] { 3, 99 })
                db.Notifications.Add(new UserAccountLifecycleChangedNotification
                {
                    Id = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(),
                    ActionValue = action, SentAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync(ct);
            await Assert.That(() => UserLifecycleAuditRepair.RunAsync(db, 2, ct)).Throws<InvalidOperationException>();
            db.ChangeTracker.Clear();
            await Assert.That(await db.Notifications.CountAsync(row => row.UserLifecycleAction == null, ct)).IsEqualTo(2);
        });
    }
}
