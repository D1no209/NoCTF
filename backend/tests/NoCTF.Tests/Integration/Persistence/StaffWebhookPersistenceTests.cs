using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.StaffWebhooks;
using NoCTF.Tests.Fixtures.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("StaffWebhooks")]
[Category("Integration")]
public sealed class StaffWebhookPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Snapshot_barrier_redaction_single_claim_rotation_and_disable_preserve_private_boundary(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            var clock = new MutableClock(DateTimeOffset.UtcNow);
            await using var db = new NoCtfDbContext(options, clock); await db.Database.EnsureCreatedAsync(ct);
            var owner = Guid.NewGuid(); var judge = Guid.NewGuid(); var competition = Guid.NewGuid();
            db.Users.AddRange(new User { Id = owner, UserName = "owner", NormalizedUserName = "OWNER", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "test" },
                new User { Id = judge, UserName = "judge", NormalizedUserName = "JUDGE", Email = "judge@example.test", NormalizedEmail = "JUDGE@EXAMPLE.TEST", PasswordHash = "test" });
            db.Competitions.Add(new CtfCompetition { Id = competition, OwnerId = owner, Title = "Staff-only",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), Collaborators = [new() { CompetitionId = competition,
                    UserId = judge, Role = CompetitionCollaboratorRole.Judge }] });
            await db.SaveChangesAsync(ct);
            var protector = new NoCTF.Infrastructure.Authentication.PlatformSecretProtector(Microsoft.Extensions.Options.Options.Create(
                new NoCTF.Infrastructure.Authentication.EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
            var settings = new NoCTF.Infrastructure.Competitions.Webhooks.CompetitionWebhookOptions(new("https://noctf.example/"), 10,
                new HashSet<string>(), new HashSet<string>());
            var store = new StaffWebhookStore(db, protector, settings, clock);
            var created = await store.SaveAsync(new(competition, owner, null, "Staff BOT", "https://bot.example/staff/private-key", true,
                [StaffWorkItemKind.Consultation]), ct);
            await Assert.That(created.Succeeded).IsTrue();
            var target = created.Value!.Target.Id;
            await Assert.That(created.Value.SigningSecret!.StartsWith("whsec_", StringComparison.Ordinal)).IsTrue();
            var read = await store.ListAsync(competition, judge, 0, 20, ct);
            await Assert.That(read.Value!.CanManage).IsFalse();
            await Assert.That(read.Value.Items.Single().EndpointUrl).IsNull();
            await Assert.That((await store.RotateAsync(competition, target, judge, ct)).Succeeded).IsFalse();
            var claims = await store.ClaimAsync(20, ct);
            await Assert.That(claims.Count).IsEqualTo(1);
            var delivery = await store.PrepareAsync(claims[0], ct);
            await Assert.That(delivery.Delivery!.RequireNoContent).IsTrue();
            await Assert.That((await store.PrepareAsync(claims[0], ct)).Delivery).IsNull();
            using var json = System.Text.Json.JsonDocument.Parse(delivery.Delivery.Body!);
            await Assert.That(json.RootElement.GetProperty("data").GetProperty("snapshotReason").GetString()).IsEqualTo("Initial");
            await store.CompleteAsync(claims[0], 204, true, false, null, ct);
            await Assert.That((await db.StaffWebhookTargets.SingleAsync(ct)).ActiveSnapshotId).IsNull();
            var rotated = await store.RotateAsync(competition, target, owner, ct);
            await Assert.That(rotated.Value!.SigningSecret == created.Value.SigningSecret).IsFalse();
            var test = await store.TestAsync(competition, target, owner, ct); await Assert.That(test.Succeeded).IsTrue();
            var testClaim = (await store.ClaimAsync(20, ct)).Single();
            var testBody = await store.PrepareAsync(testClaim, ct);
            await Assert.That(testBody.Delivery!.PreviousSigningSecret).IsEqualTo(created.Value.SigningSecret);
            await store.CompleteAsync(testClaim, 500, false, false, null, ct);
            clock.Advance(TimeSpan.FromMinutes(4)); await store.TickAsync(false, ct);
            var due = await store.ClaimAsync(20, ct);
            foreach (var claim in due)
            {
                var prepared = await store.PrepareAsync(claim, ct);
                if (prepared.Delivery is null) continue;
                using var body = System.Text.Json.JsonDocument.Parse(prepared.Delivery.Body!);
                if (body.RootElement.GetProperty("type").GetString() == "com.noctf.staff.heartbeat.v1")
                    await store.CompleteAsync(claim, 204, true, false, null, ct);
            }
            await Assert.That((await db.StaffWebhookTargets.SingleAsync(ct)).ActiveSnapshotId).IsNotNull();
            await Assert.That(await db.StaffWebhookEvents.AnyAsync(value => value.SnapshotReason == StaffSnapshotReason.Resync, ct)).IsTrue();
            var stopped = await store.SaveAsync(new(competition, owner, target, "Staff BOT", "https://bot.example/staff/private-key", false,
                [StaffWorkItemKind.Consultation]), ct);
            await Assert.That(stopped.Succeeded).IsTrue();
            var stoppedClaims = await store.ClaimAsync(20, ct);
            var controlCount = 0;
            foreach (var claim in stoppedClaims)
            {
                var prepared = await store.PrepareAsync(claim, ct);
                if (prepared.Delivery is null) continue;
                using var body = System.Text.Json.JsonDocument.Parse(prepared.Delivery.Body!);
                await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo("com.noctf.staff.subscription.disabled.v1"); controlCount++;
            }
            await Assert.That(controlCount).IsEqualTo(1);
        });
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
    [Test]
    [Timeout(300_000)]
    public async Task Source_and_summary_commit_together_and_staff_opened_consultation_waits_for_participant(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var now = DateTimeOffset.UtcNow; var actorId = Guid.NewGuid(); var competitionId = Guid.NewGuid(); var rootId = Guid.NewGuid();
            db.Users.Add(new User { Id = actorId, UserName = "staff", Email = "staff@example.test", PasswordHash = "test", CreatedAt = now, UpdatedAt = now });
            db.Competitions.Add(new CtfCompetition { Id = competitionId, OwnerId = actorId, Title = "Draft", ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf) });
            await db.SaveChangesAsync(ct);
            var root = new QuestionOpenedNotification { Id = rootId, CompetitionId = competitionId, TeamId = Guid.NewGuid(),
                SourceId = actorId, SourceType = NotificationSourceType.User, TargetType = NotificationTargetType.CompetitionCollaborators,
                TargetId = competitionId, SentAt = now, Body = "PRIVATE_WRITEUP_TITLE_AND_BODY", QuestionSubject = CompetitionQuestionSubject.Platform,
                QuestionStatus = CompetitionQuestionStatus.Pending, QuestionActorRole = CompetitionQuestionParticipantRole.Judge };
            db.Notifications.Add(root); await db.SaveChangesAsync(ct);
            var item = await db.StaffWebhookWorkItems.SingleAsync(ct);
            await Assert.That(item.Summary.RequiresStaffAction).IsFalse();
            var first = await db.StaffWebhookEvents.SingleAsync(ct);
            await Assert.That(first.Sequence).IsEqualTo(1L);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                db.Notifications.Add(new MessageNotification { Id = Guid.NewGuid(), ThreadRootId = rootId, CompetitionId = competitionId,
                    SourceId = actorId, SentAt = now.AddSeconds(1), QuestionStatus = CompetitionQuestionStatus.Pending,
                    QuestionActorRole = CompetitionQuestionParticipantRole.Participant, Body = "PRIVATE_PARTICIPANT_REPLY" });
                await db.SaveChangesAsync(ct);
                await Assert.That((await db.StaffWebhookWorkItems.SingleAsync(ct)).Summary.RequiresStaffAction).IsTrue();
                await transaction.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
            await Assert.That(await db.StaffWebhookEvents.CountAsync(ct)).IsEqualTo(1);
            await Assert.That((await db.StaffWebhookWorkItems.SingleAsync(ct)).Summary.RequiresStaffAction).IsFalse();
            var body = System.Text.Encoding.UTF8.GetString(StaffWebhookProtocol.Serialize(first, Guid.NewGuid(), new("https://noctf.example/")));
            await Assert.That(body.Contains("PRIVATE", StringComparison.Ordinal)).IsFalse();
        });
    }
}
