using System.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionEvents")]
public sealed class CompetitionEventPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Immutable_feed_enforces_visibility_export_and_audited_flag_access(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_competition_events")
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
                    Human(ids.OwnerId, "owner", UserRole.Organizer, now),
                    Human(ids.ManagerId, "manager", UserRole.Organizer, now),
                    Human(ids.JudgeId, "judge", UserRole.Organizer, now),
                    Human(ids.ObserverId, "observer", UserRole.Organizer, now),
                    Human(ids.TeamAUserId, "team-a-user", UserRole.User, now),
                    Human(ids.TeamBUserId, "team-b-user", UserRole.User, now),
                    Human(ids.ParticipantId, "participant", UserRole.User, now),
                    Bot(ids.BotId, now));
                setup.Competitions.Add(new Competition
                {
                    Id = ids.CompetitionId,
                    OwnerId = ids.OwnerId,
                    ManagerIds = [ids.ManagerId],
                    JudgeIds = [ids.JudgeId],
                    ObserverIds = [ids.ObserverId],
                    Title = "Event feed competition",
                    Mode = GameMode.Ctf,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    ConfigurationUpdatedAt = now,
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.Challenges.Add(new Challenge
                {
                    Id = ids.ChallengeId,
                    OwnerId = ids.OwnerId,
                    Mode = GameMode.Ctf,
                    Visibility = ChallengeVisibility.Shared,
                    Title = "Event challenge",
                    Direction = "Web",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = ids.CompetitionChallengeId,
                    CompetitionId = ids.CompetitionId,
                    ChallengeId = ids.ChallengeId,
                    BaseScore = 500,
                    IsPublished = true,
                    UpdatedAt = now,
                    Hints =
                    [
                        new CompetitionChallengeHint
                        {
                            Id = ids.HintId,
                            Content = "Traceable hint",
                            PublishedAt = now.AddMinutes(-5)
                        }
                    ]
                });
                setup.Teams.AddRange(
                    ApprovedTeam(ids.TeamAId, ids.CompetitionId, "Team A", ids.TeamAUserId, now),
                    ApprovedTeam(ids.TeamBId, ids.CompetitionId, "Team B", ids.TeamBUserId, now));
                setup.GameplayFacts.Add(new GameplayFact
                {
                    Id = ids.GameplayFactId,
                    CompetitionId = ids.CompetitionId,
                    CompetitionChallengeId = ids.CompetitionChallengeId,
                    TeamId = ids.TeamAId,
                    ActorUserId = ids.TeamAUserId,
                    Kind = GameplayFactKind.FlagAttempt,
                    Value = "flag{competition-event-secret}",
                    ValueSha256 = SHA256.HashData(
                        Encoding.UTF8.GetBytes("flag{competition-event-secret}")),
                    OccurredAt = now.AddMinutes(-2),
                    State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Correct,
                    UpdatedAt = now.AddMinutes(-2)
                });
                await setup.SaveChangesAsync(ct);
            }

            var outbox = new RecordingOutbox();
            await using var db = new NoCtfDbContext(options);
            var store = new CompetitionEventStore(db, outbox);
            var eventIds = new List<Guid>
            {
                await store.RecordAsync(new(
                    ids.CompetitionId,
                    CompetitionEventKind.HintPublished,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Public,
                    now.AddMinutes(-5),
                    CompetitionChallengeId: ids.CompetitionChallengeId,
                    HintId: ids.HintId), ct),
                await store.RecordAsync(new(
                    ids.CompetitionId,
                    CompetitionEventKind.CompetitionLifecycleChanged,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Public,
                    now.AddMinutes(-4),
                    CompetitionStatus: CompetitionStatus.Running), ct),
                await store.RecordAsync(new(
                    ids.CompetitionId,
                    CompetitionEventKind.GameplayFactReceived,
                    CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Team,
                    now.AddMinutes(-3),
                    ActorUserId: ids.TeamAUserId,
                    TeamId: ids.TeamAId,
                    CompetitionChallengeId: ids.CompetitionChallengeId,
                    GameplayFactId: ids.GameplayFactId,
                    GameplayFactKind: GameplayFactKind.FlagAttempt), ct),
                await store.RecordAsync(new(
                    ids.CompetitionId,
                    CompetitionEventKind.RuntimeStateChanged,
                    CompetitionEventLevel.Warning,
                    CompetitionEventVisibility.Team,
                    now.AddMinutes(-2),
                    TeamId: ids.TeamBId,
                    CompetitionChallengeId: ids.CompetitionChallengeId), ct),
                await store.RecordAsync(new(
                    ids.CompetitionId,
                    CompetitionEventKind.CompetitionUpdated,
                    CompetitionEventLevel.Error,
                    CompetitionEventVisibility.Staff,
                    now.AddMinutes(-1),
                    ActorUserId: ids.ManagerId,
                    Reason: "token=super-secret-token"), ct)
            };
            await db.SaveChangesAsync(ct);

            var participant = await store.QueryAsync(
                Query(ids, ids.ParticipantId, now), ct);
            await Assert.That(participant.AccessLevel)
                .IsEqualTo(CompetitionEventAccessLevel.Participant);
            await Assert.That(participant.Items!).Count().IsEqualTo(2);
            await Assert.That(participant.Items!.Any(item => item.HintId == ids.HintId))
                .IsTrue();

            var teamA = await store.QueryAsync(Query(ids, ids.TeamAUserId, now), ct);
            await Assert.That(teamA.AccessLevel).IsEqualTo(CompetitionEventAccessLevel.Team);
            await Assert.That(teamA.ViewerTeamId).IsEqualTo(ids.TeamAId);
            await Assert.That(teamA.Items!).Count().IsEqualTo(3);
            await Assert.That(teamA.Items!.Any(item => item.TeamId == ids.TeamBId)).IsFalse();
            var submissionEvent = teamA.Items!.Single(item => item.TeamId == ids.TeamAId);
            await Assert.That(submissionEvent.TeamDisplayName)
                .IsEqualTo("Team A");
            await Assert.That(submissionEvent.CompetitionChallengeId)
                .IsEqualTo(ids.CompetitionChallengeId);
            await Assert.That(submissionEvent.ChallengeTitle)
                .IsEqualTo("Event challenge");

            var observer = await store.QueryAsync(Query(ids, ids.ObserverId, now), ct);
            await Assert.That(observer.AccessLevel).IsEqualTo(CompetitionEventAccessLevel.Staff);
            await Assert.That(observer.Items!).Count().IsEqualTo(5);
            await Assert.That(observer.CanExport).IsFalse();
            await Assert.That(observer.CanAccessGameplayFactValues).IsFalse();
            await Assert.That(observer.Items!.Single(item => item.Kind == CompetitionEventKind.CompetitionUpdated).Reason)
                .DoesNotContain("super-secret-token");

            var warningOnly = await store.QueryAsync(
                Query(ids, ids.ManagerId, now) with
                {
                    MinimumLevel = CompetitionEventLevel.Warning,
                    Limit = 1
                }, ct);
            await Assert.That(warningOnly.Items!).Count().IsEqualTo(1);
            await Assert.That(warningOnly.Items![0].Level).IsEqualTo(CompetitionEventLevel.Error);
            var nextPage = await store.QueryAsync(
                Query(ids, ids.ManagerId, now) with
                {
                    MinimumLevel = CompetitionEventLevel.Warning,
                    BeforeOccurredAt = warningOnly.Items[0].OccurredAt,
                    BeforeId = warningOnly.Items[0].Id,
                    Limit = 1
                }, ct);
            await Assert.That(nextPage.Items!).Count().IsEqualTo(1);
            await Assert.That(nextPage.Items![0].Level).IsEqualTo(CompetitionEventLevel.Warning);

            var observerExport = await store.ExportAsync(Query(ids, ids.ObserverId, now), ct);
            await Assert.That(observerExport.State).IsEqualTo(CompetitionEventReadState.Forbidden);
            var managerExport = await store.ExportAsync(Query(ids, ids.ManagerId, now), ct);
            await Assert.That(managerExport.State).IsEqualTo(CompetitionEventReadState.Available);
            using var reader = new StreamReader(managerExport.Export!.Content, Encoding.UTF8);
            var export = await reader.ReadToEndAsync(ct);
            await Assert.That(export.Split('\n', StringSplitOptions.RemoveEmptyEntries)).Count()
                .IsEqualTo(5);
            await Assert.That(export).DoesNotContain("super-secret-token");

            var observerFlag = await store.AccessGameplayFactValueAsync(new(
                ids.CompetitionId,
                ids.GameplayFactId,
                ids.ObserverId,
                "Need incident review",
                now), ct);
            await Assert.That(observerFlag.State).IsEqualTo(CompetitionEventReadState.Forbidden);
            var judgeFlag = await store.AccessGameplayFactValueAsync(new(
                ids.CompetitionId,
                ids.GameplayFactId,
                ids.JudgeId,
                "Investigate flag{competition-event-secret} incident",
                now), ct);
            await Assert.That(judgeFlag.State).IsEqualTo(CompetitionEventReadState.Available);
            await Assert.That(judgeFlag.View!.Value)
                .IsEqualTo("flag{competition-event-secret}");
            var flagAudit = await db.CompetitionEvents.AsNoTracking().SingleAsync(
                item => item.Kind == CompetitionEventKind.ProtectedGameplayFactValueAccessed,
                ct);
            await Assert.That(flagAudit.ActorUserId).IsEqualTo(ids.JudgeId);
            await Assert.That(flagAudit.Reason).Contains("[REDACTED]");
            await Assert.That(flagAudit.Reason).DoesNotContain("competition-event-secret");

            var bot = await store.QueryAsync(Query(ids, ids.BotId, now), ct);
            await Assert.That(bot.State).IsEqualTo(CompetitionEventReadState.Forbidden);
            await Assert.That(outbox.Messages.OfType<CompetitionEventCommitted>()).Count()
                .IsEqualTo(6);

            var immutable = await db.CompetitionEvents.SingleAsync(
                item => item.Id == eventIds[0], ct);
            immutable.PayloadJson = """{"schemaVersion":1,"reason":"attempted mutation"}""";
            await Assert.That(async () => await db.SaveChangesAsync(ct))
                .Throws<InvalidOperationException>();
        });
    }

    private static CompetitionEventQuery Query(
        TestIds ids,
        Guid userId,
        DateTimeOffset now) =>
        new(
            ids.CompetitionId,
            userId,
            Kind: null,
            MinimumLevel: null,
            TeamId: null,
            ActorUserId: null,
            CompetitionChallengeId: null,
            RuntimeInstanceId: null,
            From: now.AddHours(-1),
            To: now.AddHours(1),
            BeforeOccurredAt: null,
            BeforeId: null,
            Limit: 200);

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
            UserName = "event-bot",
            NormalizedUserName = "EVENT-BOT",
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
        Guid userId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = userId,
            MemberIds = [userId],
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
        public Guid HintId { get; } = Guid.CreateVersion7();
        public Guid ChallengeId { get; } = Guid.CreateVersion7();
        public Guid GameplayFactId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid OwnerId { get; } = Guid.CreateVersion7();
        public Guid ManagerId { get; } = Guid.CreateVersion7();
        public Guid JudgeId { get; } = Guid.CreateVersion7();
        public Guid ObserverId { get; } = Guid.CreateVersion7();
        public Guid TeamAUserId { get; } = Guid.CreateVersion7();
        public Guid TeamBUserId { get; } = Guid.CreateVersion7();
        public Guid ParticipantId { get; } = Guid.CreateVersion7();
        public Guid BotId { get; } = Guid.CreateVersion7();
        public Guid TeamAId { get; } = Guid.CreateVersion7();
        public Guid TeamBId { get; } = Guid.CreateVersion7();
    }
}
