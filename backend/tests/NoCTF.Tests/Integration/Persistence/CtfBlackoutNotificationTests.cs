using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CtfBlackoutNotificationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Queued_blood_is_suppressed_at_blackout_boundary_but_announcements_remain(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("blackout_notifications").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var now = clock.GetUtcNow();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options, clock);
            await db.Database.EnsureCreatedAsync(ct);
            var owner = new User { Id = Guid.NewGuid(), UserName = "owner", Email = "owner@example.test",
                PasswordHash = "test", Kind = UserKind.Human, CreatedAt = now, UpdatedAt = now };
            var competition = new CtfCompetition { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "CTF",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32],
                Status = CompetitionStatus.Running, StartAt = now.AddHours(-1), EndAt = now.AddHours(1),
                HiddenStartAt = now.AddSeconds(1), CreatedAt = now, UpdatedAt = now };
            db.Users.Add(owner);
            db.Competitions.Add(competition);
            await db.SaveChangesAsync(ct);
            var delivery = new CompetitionNotificationDelivery(db, clock: clock);
            var challenge = Guid.NewGuid();
            var payload = new BloodAwardedPayload(competition.Id, challenge, "Question", LeaderboardBloodRank.First,
                Guid.NewGuid(), "Team", now.AddMinutes(-1));
            await delivery.DeliverAsync(competition.Id, challenge, NotificationKind.BloodAwarded, "before", payload, null, ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(1);
            clock.Advance(TimeSpan.FromSeconds(1));
            foreach (var rank in Enum.GetValues<LeaderboardBloodRank>())
                await delivery.DeliverAsync(competition.Id, challenge, NotificationKind.BloodAwarded,
                    "queued-" + rank, payload with { BloodRank = rank }, null, ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(1);
            await delivery.PublishAsync(new(competition.Id, owner.Id, "Notice", "Maintenance",
                CompetitionAnnouncementAudience.Participants, clock.GetUtcNow()), ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(2);
            // 直接修改数据库，验证消费者不会采用跟踪实体中的旧可见性。
            await db.Competitions.Where(c => c.Id == competition.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.HiddenStartAt, (DateTimeOffset?)null)
                    .SetProperty(c => c.FrozenStartAt, now), ct);
            await delivery.DeliverAsync(competition.Id, challenge, NotificationKind.BloodAwarded, "frozen", payload, null, ct);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(3);
        });
    }
}
