using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Events;
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
    public async Task Cheat_and_appeal_lifecycles_emit_complete_safe_summaries_without_protected_values(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
            var owner = Guid.NewGuid(); var competition = Guid.NewGuid(); var teamId = Guid.NewGuid(); var templateId = Guid.NewGuid(); var challengeId = Guid.NewGuid();
            db.Users.Add(new User { Id = owner, UserName = "owner", NormalizedUserName = "OWNER", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "test" });
            db.Competitions.Add(new CtfCompetition { Id = competition, OwnerId = owner, Title = "Private", ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf) });
            db.Challenges.Add(new CtfChallenge { Id = templateId, OwnerId = owner, Title = "Private puzzle", Direction = new string('W', 96), Definition = TestConfigurations.Definition(GameMode.Ctf) });
            db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = challengeId, CompetitionId = competition, ChallengeId = templateId, Rules = TestConfigurations.Rules(GameMode.Ctf) });
            db.Teams.Add(new Team { Id = teamId, CompetitionId = competition, Name = "Blue", CaptainId = owner, MemberIds = [owner], InvitationToken = new string('T', 32), IsBanned = true });
            db.StaffWebhookTargets.Add(new() { Id = Guid.NewGuid(), CompetitionId = competition, ApprovedById = owner,
                Name = "Staff", EndpointUrl = "https://bot.example/staff", Enabled = true });
            await db.SaveChangesAsync(ct);
            var factId = Guid.NewGuid(); var banId = Guid.NewGuid(); var appealId = Guid.NewGuid();
            db.GameplayFacts.Add(new FlagAttemptGameplayFact { Id = factId, CompetitionId = competition, CompetitionChallengeId = challengeId,
                TeamId = teamId, ActorUserId = owner, Value = "flag{DO_NOT_SHARE}", OccurredAt = now, UpdatedAt = now,
                State = GameplayFactState.Completed, Result = GameplayFactResult.Rejected, FailureCode = GameplayFactFailureCode.StaticFlagWithoutAttachment });
            db.CompetitionEvents.Add(new TeamBannedEvent { Id = banId, CompetitionId = competition, TeamId = teamId, SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.Team,
                SubjectId = teamId, ActorUserId = owner, OccurredAt = now, Reason = "PRIVATE_BAN_REASON" });
            db.CompetitionEvents.Add(new TeamBanAppealSubmittedEvent { Id = appealId, CompetitionId = competition, TeamId = teamId,
                SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.Team, SubjectId = teamId, ParentEventId = banId,
                ActorUserId = owner, OccurredAt = now.AddSeconds(1), Reason = "PRIVATE_APPEAL_STATEMENT" });
            await db.SaveChangesAsync(ct);
            var current = await db.StaffWebhookWorkItems.OrderBy(value => value.Kind).ToArrayAsync(ct);
            await Assert.That(current.Length).IsEqualTo(2);
            await Assert.That(current.All(value => value.Summary.RequiresStaffAction)).IsTrue();
            await Assert.That(current[0].Summary.RelatedTeamId).IsNull();
            await Assert.That(current[0].Summary.Direction!.Length).IsEqualTo(96);
            db.CompetitionEvents.Add(new CheatIncidentDismissedEvent { Id = Guid.NewGuid(), CompetitionId = competition,
                GameplayFactId = factId, SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.GameplayFact, SubjectId = factId,
                OccurredAt = now.AddSeconds(2), ActorUserId = owner, Reason = "PRIVATE_RESOLUTION" });
            db.CompetitionEvents.Add(new TeamBanAppealUpheldEvent { Id = Guid.NewGuid(), CompetitionId = competition,
                TeamId = teamId, SubjectType = NoCTF.Domain.Shared.EntityReferenceKind.Team, SubjectId = teamId, ParentEventId = appealId,
                OccurredAt = now.AddSeconds(2), ActorUserId = owner, Reason = "PRIVATE_APPEAL_RESOLUTION" });
            await db.SaveChangesAsync(ct);
            var terminal = await db.StaffWebhookWorkItems.ToArrayAsync(ct);
            await Assert.That(terminal.All(value => !value.Summary.RequiresStaffAction)).IsTrue();
            var events = await db.StaffWebhookEvents.ToArrayAsync(ct);
            await Assert.That(events.Length).IsEqualTo(4);
            foreach (var item in events)
            {
                var payload = System.Text.Encoding.UTF8.GetString(StaffWebhookProtocol.Serialize(item, Guid.NewGuid(), new("https://noctf.example/")));
                await Assert.That(payload.Contains("DO_NOT_SHARE", StringComparison.Ordinal) || payload.Contains("PRIVATE_", StringComparison.Ordinal)).IsFalse();
            }
        });
    }
    [Test]
    [Timeout(300_000)]
    public async Task Multi_page_snapshot_gates_new_events_and_competing_claims_and_revoked_authorization_fail_closed(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString,
                value => value.MigrationsAssembly(typeof(NoCTF.Persistence.PostgreSql.PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            var clock = new MutableClock(DateTimeOffset.UtcNow);
            await using var db = new NoCtfDbContext(options, clock); await db.Database.MigrateAsync(ct);
            var owner = Guid.NewGuid(); var replacement = Guid.NewGuid(); var competition = Guid.NewGuid();
            db.Users.AddRange(new User { Id = owner, UserName = "owner", NormalizedUserName = "OWNER", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", PasswordHash = "test" },
                new User { Id = replacement, UserName = "replacement", NormalizedUserName = "REPLACEMENT", Email = "replacement@example.test", NormalizedEmail = "REPLACEMENT@EXAMPLE.TEST", PasswordHash = "test" });
            db.Competitions.Add(new CtfCompetition { Id = competition, OwnerId = owner, Title = "Private staff event", ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf) });
            await db.SaveChangesAsync(ct);
            for (var index = 0; index < 105; index++) db.Notifications.Add(Question(Guid.NewGuid()));
            await db.SaveChangesAsync(ct);
            await Assert.That(await db.StaffWebhookEvents.CountAsync(ct)).IsEqualTo(0);
            var protector = new NoCTF.Infrastructure.Authentication.PlatformSecretProtector(Microsoft.Extensions.Options.Options.Create(
                new NoCTF.Infrastructure.Authentication.EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
            var settings = new NoCTF.Infrastructure.Competitions.Webhooks.CompetitionWebhookOptions(new("https://noctf.example/"), 10, new HashSet<string>(), new HashSet<string>());
            var store = new StaffWebhookStore(db, protector, settings, clock);
            var created = await store.SaveAsync(new(competition, owner, null, "Staff", "https://bot.example/staff", true, [StaffWorkItemKind.Consultation]), ct);
            await Assert.That(created.Succeeded).IsTrue();
            var target = created.Value!.Target.Id;
            var pages = await db.StaffWebhookEvents.Where(value => value.Kind == StaffWebhookEventKind.PendingSnapshot).OrderBy(value => value.PageIndex).ToArrayAsync(ct);
            await Assert.That(pages.Length).IsEqualTo(2);
            await Assert.That(pages[0].Items.Count).IsEqualTo(100);
            await Assert.That(pages[1].Items.Count).IsEqualTo(5);
            await Assert.That(pages[0].AsOfSequence).IsEqualTo(pages[1].AsOfSequence);
            var late = Guid.NewGuid(); db.Notifications.Add(Question(late)); await db.SaveChangesAsync(ct);
            var deltaId = await db.StaffWebhookEvents.Where(value => value.Items.Any(item => item.Summary.Id == late)).Select(value => value.Id).SingleAsync(ct);
            await store.TickAsync(false, ct);
            for (var scan = 0; scan < 5 && await db.StaffWebhookEvents.AnyAsync(value => value.DispatchedAt == null, ct); scan++)
                await store.TickAsync(false, ct);
            await Assert.That(await db.StaffWebhookEvents.AnyAsync(value => value.DispatchedAt == null, ct)).IsFalse();
            await using var competitor = new NoCtfDbContext(options, clock);
            var otherStore = new StaffWebhookStore(competitor, protector, settings, clock);
            var claims = (await Task.WhenAll(store.ClaimAsync(200, ct), otherStore.ClaimAsync(200, ct))).SelectMany(value => value).ToArray();
            await Assert.That(claims.Select(value => (value.EventId, value.TargetId)).Distinct().Count()).IsEqualTo(claims.Length);
            var delta = claims.Single(value => value.EventId == deltaId);
            await Assert.That((await store.PrepareAsync(delta, ct)).Deferred).IsTrue();
            foreach (var page in pages)
            {
                var claim = claims.Single(value => value.EventId == page.Id);
                await Assert.That((await store.PrepareAsync(claim, ct)).Delivery).IsNotNull();
                await store.CompleteAsync(claim, 204, true, false, null, ct);
            }
            await Assert.That((await db.StaffWebhookTargets.SingleAsync(ct)).ActiveSnapshotId).IsNull();
            clock.Advance(TimeSpan.FromSeconds(6));
            var retry = (await store.ClaimAsync(200, ct)).Single(value => value.EventId == deltaId);
            db.ChangeTracker.Clear();
            (await db.Competitions.SingleAsync(ct)).OwnerId = replacement; await db.SaveChangesAsync(ct);
            await Assert.That((await store.PrepareAsync(retry, ct)).Delivery).IsNull();
            await Assert.That((await store.TestAsync(Guid.NewGuid(), target, owner, ct)).Succeeded).IsFalse();
            clock.Advance(TimeSpan.FromSeconds(60)); await store.TickAsync(false, ct);
            await Assert.That((await db.StaffWebhookTargets.SingleAsync(ct)).AuthorizationRevoked).IsTrue();

            Notification Question(Guid id) => new QuestionOpenedNotification { Id = id, SourceId = owner,
                TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = competition, TeamId = Guid.NewGuid(),
                QuestionSubject = CompetitionQuestionSubject.Platform, QuestionStatus = CompetitionQuestionStatus.Pending,
                QuestionActorRole = CompetitionQuestionParticipantRole.Asker, SentAt = clock.GetUtcNow(), Body = "PRIVATE_CONTENT_EXCLUDED" };
        });
    }
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
            db.StaffWebhookTargets.Add(new() { Id = Guid.NewGuid(), CompetitionId = competitionId, ApprovedById = actorId,
                Name = "Approved test receiver", EndpointUrl = "https://bot.example.test/staff/test", Enabled = true });
            await db.SaveChangesAsync(ct);
            var root = new QuestionOpenedNotification { Id = rootId, TeamId = Guid.NewGuid(),
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
                db.Notifications.Add(new MessageNotification { Id = Guid.NewGuid(), ThreadRootId = rootId,
                    TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = competitionId,
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
