using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Questions;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionQuestions")]
public sealed class CompetitionQuestionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Private_dialogue_enforces_roles_audits_and_private_notification_delivery(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_questions")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ids = new TestIds();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.AddRange(
                    Human(ids.AdministratorId, "administrator", UserRole.Administrator, now),
                    Human(ids.OwnerId, "competition-owner", UserRole.Organizer, now),
                    Human(ids.ManagerId, "competition-manager", UserRole.Organizer, now),
                    Human(ids.JudgeId, "competition-judge", UserRole.Organizer, now),
                    Human(ids.ObserverId, "competition-observer", UserRole.Organizer, now),
                    Human(ids.AuthorId, "challenge-author", UserRole.Organizer, now),
                    Human(ids.AuthorManagerId, "challenge-manager", UserRole.Organizer, now),
                    Human(ids.AskerId, "question-asker", UserRole.User, now),
                    Human(ids.OtherParticipantId, "other-participant", UserRole.User, now),
                    Bot(ids.BotId, now));
                setup.Competitions.Add(new Competition
                {
                    Id = ids.CompetitionId,
                    OwnerId = ids.OwnerId,
                    ManagerIds = [ids.ManagerId],
                    JudgeIds = [ids.JudgeId],
                    ObserverIds = [ids.ObserverId],
                    Title = "Question competition",
                    Mode = GameMode.Ctf,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    ConfigurationUpdatedAt = now,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddMinutes(-10),
                    EndAt = now.AddHours(1),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Challenges.Add(new Challenge
                {
                    Id = ids.ChallengeId,
                    OwnerId = ids.AuthorId,
                    ManagerIds = [ids.AuthorManagerId],
                    Mode = GameMode.Ctf,
                    Visibility = ChallengeVisibility.Private,
                    Title = "Question template",
                    Direction = "Web",
                    DefinitionJson = """{"schemaVersion":1}""",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = ids.CompetitionChallengeId,
                    CompetitionId = ids.CompetitionId,
                    ChallengeId = ids.ChallengeId,
                    BaseScore = 500,
                    Order = 1,
                    IsPublished = true,
                    RulesJson = """{"schemaVersion":1}""",
                    UpdatedAt = now
                });
                setup.Teams.AddRange(
                    ApprovedTeam(ids.AskerTeamId, ids.CompetitionId, "Asker team", ids.AskerId, now),
                    ApprovedTeam(ids.OtherTeamId, ids.CompetitionId, "Other team", ids.OtherParticipantId, now));
                await setup.SaveChangesAsync(ct);
            }

            await using var db = new NoCtfDbContext(options);
            var outbox = new RecordingOutbox();
            var store = new CompetitionQuestionStore(
                db,
                outbox,
                new CompetitionEventStore(db, outbox),
                NullLogger<CompetitionQuestionStore>.Instance);
            var created = await store.CreateAsync(new(
                ids.CompetitionId,
                CompetitionQuestionSubject.Challenge,
                ids.CompetitionChallengeId,
                null,
                ids.AskerId,
                "The service does not start",
                "The documented command exits before the health check.",
                now), ct);

            await Assert.That(created.Failure).IsNull();
            await Assert.That(created.Question!.Status)
                .IsEqualTo(CompetitionQuestionStatus.Pending);
            var opened = outbox.Messages.OfType<DeliverCompetitionQuestionNotification>().Single();
            await Assert.That(opened.RecipientUserIds).IsEquivalentTo([
                ids.AdministratorId,
                ids.OwnerId,
                ids.ManagerId,
                ids.JudgeId,
                ids.AuthorId,
                ids.AuthorManagerId
            ]);
            await Assert.That(opened.RecipientUserIds).DoesNotContain(ids.ObserverId);
            await Assert.That(opened.ToString()).DoesNotContain("health check");

            var observerView = await store.FindAsync(
                ids.CompetitionId,
                created.Question.Id,
                ids.ObserverId,
                ct);
            var administratorView = await store.FindAsync(
                ids.CompetitionId,
                created.Question.Id,
                ids.AdministratorId,
                ct);
            var authorView = await store.FindAsync(
                ids.CompetitionId,
                created.Question.Id,
                ids.AuthorId,
                ct);
            var hiddenView = await store.FindAsync(
                ids.CompetitionId,
                created.Question.Id,
                ids.OtherParticipantId,
                ct);
            await Assert.That(observerView!.Access).IsEqualTo(CompetitionQuestionAccess.Observer);
            await Assert.That(administratorView!.Access).IsEqualTo(CompetitionQuestionAccess.Handler);
            await Assert.That(authorView!.Access).IsEqualTo(CompetitionQuestionAccess.Handler);
            await Assert.That(hiddenView).IsNull();
            db.ChangeTracker.Clear();

            var replied = await store.AddMessageAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.AuthorId,
                "Use the HTTP service port, not the internal metrics port.",
                0,
                now.AddSeconds(1)), ct);
            await Assert.That(replied.Question!.Status)
                .IsEqualTo(CompetitionQuestionStatus.Replied);
            await Assert.That(replied.Question.Entries).Count().IsEqualTo(2);
            var replyEntry = replied.Question.Entries.Single(entry =>
                entry.Kind == CompetitionQuestionEntryKind.Message
                && entry.Body == "Use the HTTP service port, not the internal metrics port.");
            await Assert.That(replyEntry.ActorRole)
                .IsEqualTo(CompetitionQuestionParticipantRole.ChallengeOwner);
            await Assert.That(replied.Question.Entries[0].Body)
                .IsEqualTo("The documented command exits before the health check.");
            var replyNotification = outbox.Messages
                .OfType<DeliverCompetitionQuestionNotification>()
                .Last();
            await Assert.That(replyNotification.RecipientUserIds)
                .IsEquivalentTo([ids.AskerId]);
            db.ChangeTracker.Clear();

            var resolved = await store.ChangeStatusAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.AskerId,
                CompetitionQuestionStatus.Resolved,
                replied.Question.Revision,
                now.AddSeconds(2)), ct);
            await Assert.That(resolved.Question!.Status)
                .IsEqualTo(CompetitionQuestionStatus.Resolved);
            db.ChangeTracker.Clear();

            var reopened = await store.AddMessageAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.AskerId,
                "The same issue returns after a runtime reset.",
                resolved.Question.Revision,
                now.AddSeconds(3)), ct);
            await Assert.That(reopened.Question!.Status)
                .IsEqualTo(CompetitionQuestionStatus.Pending);
            db.ChangeTracker.Clear();

            var secondReply = await store.AddMessageAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.ManagerId,
                "After reset, wait for the replacement instance to become Ready.",
                reopened.Question.Revision,
                now.AddSeconds(4)), ct);
            db.ChangeTracker.Clear();
            var stillPrivate = await store.FindAsync(
                ids.CompetitionId,
                created.Question.Id,
                ids.OtherParticipantId,
                ct);
            await Assert.That(stillPrivate).IsNull();
            db.ChangeTracker.Clear();

            var stale = await store.ChangeStatusAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.ManagerId,
                CompetitionQuestionStatus.Closed,
                secondReply.Question!.Revision - 1,
                now.AddSeconds(6)), ct);
            await Assert.That(stale.Failure)
                .IsEqualTo(CompetitionQuestionFailure.RevisionConflict);
            db.ChangeTracker.Clear();
            var closed = await store.ChangeStatusAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.ManagerId,
                CompetitionQuestionStatus.Closed,
                secondReply.Question.Revision,
                now.AddSeconds(7)), ct);
            db.ChangeTracker.Clear();
            var terminal = await store.AddMessageAsync(new(
                ids.CompetitionId,
                created.Question.Id,
                ids.AskerId,
                "This must not be accepted.",
                closed.Question!.Revision,
                now.AddSeconds(8)), ct);
            await Assert.That(terminal.Failure)
                .IsEqualTo(CompetitionQuestionFailure.QuestionClosed);

            var botQuestion = await store.CreateAsync(new(
                ids.CompetitionId,
                CompetitionQuestionSubject.Platform,
                null,
                null,
                ids.BotId,
                "Bot question",
                "Automation identities cannot open dialogue.",
                now.AddSeconds(9)), ct);
            await Assert.That(botQuestion.Failure)
                .IsEqualTo(CompetitionQuestionFailure.Forbidden);

            var delivery = new CompetitionNotificationDelivery(db);
            await CompetitionNotificationMessageHandlers.Handle(opened, delivery, ct);
            await Assert.That(await db.Notifications.CountAsync(notification =>
                notification.Kind == NotificationKind.QuestionOpened
                    && notification.TargetType == NotificationTargetType.User, ct))
                .IsEqualTo(opened.RecipientUserIds.Length);
            var notificationPayloads = await db.Notifications.AsNoTracking()
                .Where(notification => notification.TargetType == NotificationTargetType.User)
                .Select(notification => notification.ContentJson)
                .ToArrayAsync(ct);
            await Assert.That(notificationPayloads.All(payload =>
                !payload.Contains("health check", StringComparison.Ordinal))).IsTrue();

            var permanentEvents = await db.CompetitionEvents.AsNoTracking()
                .Where(@event => @event.CompetitionId == ids.CompetitionId)
                .ToArrayAsync(ct);
            await Assert.That(permanentEvents.Select(@event => @event.Kind))
                .Contains(CompetitionEventKind.QuestionOpened);
            await Assert.That(permanentEvents.Select(@event => @event.Kind))
                .Contains(CompetitionEventKind.QuestionReplied);
            await Assert.That(permanentEvents.Select(@event => @event.Kind))
                .Contains(CompetitionEventKind.QuestionStatusChanged);
            await Assert.That(permanentEvents.All(@event =>
                @event.Visibility == CompetitionEventVisibility.Staff
                && !@event.PayloadJson.Contains("health check", StringComparison.Ordinal)
                && !@event.PayloadJson.Contains("runtime reset", StringComparison.Ordinal)))
                .IsTrue();
        });
    }

    private static User Human(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static User Bot(Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = "question-bot",
            NormalizedUserName = "QUESTION-BOT",
            Email = $"bot-{id:N}@bot.invalid",
            NormalizedEmail = $"BOT-{id:N}@BOT.INVALID",
            PasswordHash = "test",
            Kind = UserKind.Bot,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Team ApprovedTeam(
        Guid id,
        Guid competitionId,
        string name,
        Guid memberId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        };

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class TestIds
    {
        public Guid CompetitionId { get; } = Guid.CreateVersion7();
        public Guid CompetitionChallengeId { get; } = Guid.CreateVersion7();
        public Guid ChallengeId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid OwnerId { get; } = Guid.CreateVersion7();
        public Guid ManagerId { get; } = Guid.CreateVersion7();
        public Guid JudgeId { get; } = Guid.CreateVersion7();
        public Guid ObserverId { get; } = Guid.CreateVersion7();
        public Guid AuthorId { get; } = Guid.CreateVersion7();
        public Guid AuthorManagerId { get; } = Guid.CreateVersion7();
        public Guid AskerId { get; } = Guid.CreateVersion7();
        public Guid OtherParticipantId { get; } = Guid.CreateVersion7();
        public Guid BotId { get; } = Guid.CreateVersion7();
        public Guid AskerTeamId { get; } = Guid.CreateVersion7();
        public Guid OtherTeamId { get; } = Guid.CreateVersion7();
    }
}
