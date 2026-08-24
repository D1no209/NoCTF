using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionEvents")]
public sealed class CompetitionEventHistoryPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Staff_can_page_active_and_archived_history_while_participants_require_an_active_bounded_window(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_event_history")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var managerId = Guid.CreateVersion7();
            var judgeId = Guid.CreateVersion7();
            var observerId = Guid.CreateVersion7();
            var administratorId = Guid.CreateVersion7();
            var participantId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();

            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(cancellationToken);
                setup.Users.AddRange(
                    Human(ownerId, "history-owner", UserRole.Organizer, now),
                    Human(managerId, "history-manager", UserRole.Organizer, now),
                    Human(judgeId, "history-judge", UserRole.Organizer, now),
                    Human(observerId, "history-observer", UserRole.Organizer, now),
                    Human(administratorId, "history-admin", UserRole.Administrator, now),
                    Human(participantId, "history-participant", UserRole.User, now));
                setup.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    OwnerId = ownerId,
                    ManagerIds = [managerId],
                    JudgeIds = [judgeId],
                    ObserverIds = [observerId],
                    Title = "Permanent event history",
                    Mode = GameMode.Ctf,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddDays(-200),
                    EndAt = now.AddDays(1),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now.AddDays(-200),
                    UpdatedAt = now
                });
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var db = new NoCtfDbContext(options);
            var store = new CompetitionEventStore(db, new NullOutbox());
            var oldEventId = await store.RecordAsync(new(
                competitionId,
                CompetitionEventKind.CompetitionCreated,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                now.AddDays(-180)), cancellationToken);
            var recentEventId = await store.RecordAsync(new(
                competitionId,
                CompetitionEventKind.CompetitionLifecycleChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                now.AddHours(-1),
                CompetitionStatus: CompetitionStatus.Running), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var participantWindow = await store.QueryAsync(Query(
                competitionId,
                participantId,
                now.AddDays(-30),
                now,
                limit: 50), cancellationToken);
            await Assert.That(participantWindow.State)
                .IsEqualTo(CompetitionEventReadState.Available);
            await Assert.That(participantWindow.Items!.Select(item => item.Id))
                .IsEquivalentTo([recentEventId]);

            var participantHistory = await store.QueryAsync(Query(
                competitionId,
                participantId,
                null,
                null,
                limit: 50), cancellationToken);
            await Assert.That(participantHistory.State)
                .IsEqualTo(CompetitionEventReadState.Forbidden);

            var firstStaffPage = await store.QueryAsync(Query(
                competitionId,
                ownerId,
                null,
                null,
                limit: 1), cancellationToken);
            await Assert.That(firstStaffPage.AccessLevel)
                .IsEqualTo(CompetitionEventAccessLevel.Staff);
            await Assert.That(firstStaffPage.Items!.Select(item => item.Id))
                .IsEquivalentTo([recentEventId]);
            var firstStaffItem = firstStaffPage.Items!.Single();

            var secondStaffPage = await store.QueryAsync(Query(
                competitionId,
                ownerId,
                null,
                null,
                limit: 1) with
            {
                BeforeOccurredAt = firstStaffItem.OccurredAt,
                BeforeId = firstStaffItem.Id
            }, cancellationToken);
            await Assert.That(secondStaffPage.Items!.Select(item => item.Id))
                .IsEquivalentTo([oldEventId]);

            var list = new ListCompetitionEvents(store);
            await Assert.That((await list.ExecuteAsync(Query(
                    competitionId,
                    ownerId,
                    null,
                    null,
                    limit: 50), cancellationToken)).State)
                .IsEqualTo(CompetitionEventReadState.Available);
            var export = new ExportCompetitionEvents(store);
            await Assert.That((await export.ExecuteAsync(Query(
                    competitionId,
                    ownerId,
                    null,
                    null,
                    limit: 50), cancellationToken)).State)
                .IsEqualTo(CompetitionEventReadState.InvalidQuery);

            await db.Competitions.Where(item => item.Id == competitionId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.DeletedAt, now),
                    cancellationToken);
            foreach (var staffId in new[]
                     {
                         ownerId,
                         managerId,
                         judgeId,
                         observerId,
                         administratorId
                     })
            {
                var archivedHistory = await store.QueryAsync(Query(
                    competitionId,
                    staffId,
                    null,
                    null,
                    limit: 50), cancellationToken);
                await Assert.That(archivedHistory.State)
                    .IsEqualTo(CompetitionEventReadState.Available);
                await Assert.That(archivedHistory.AccessLevel)
                    .IsEqualTo(CompetitionEventAccessLevel.Staff);
                await Assert.That(archivedHistory.CanAccessGameplayFactValues)
                    .IsEqualTo(staffId != observerId);
                await Assert.That(archivedHistory.Items!.Select(item => item.Id))
                    .IsEquivalentTo([oldEventId, recentEventId]);
            }

            var archivedParticipant = await store.QueryAsync(Query(
                competitionId,
                participantId,
                now.AddDays(-30),
                now,
                limit: 50), cancellationToken);
            await Assert.That(archivedParticipant.State)
                .IsEqualTo(CompetitionEventReadState.CompetitionNotFound);
            var missingCompetition = await store.QueryAsync(Query(
                Guid.NewGuid(),
                ownerId,
                null,
                null,
                limit: 50), cancellationToken);
            await Assert.That(missingCompetition.State)
                .IsEqualTo(CompetitionEventReadState.CompetitionNotFound);
        });
    }

    private static CompetitionEventQuery Query(
        Guid competitionId,
        Guid userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit) =>
        new(
            competitionId,
            userId,
            Kind: null,
            MinimumLevel: null,
            TeamId: null,
            ActorUserId: null,
            CompetitionChallengeId: null,
            RuntimeInstanceId: null,
            From: from,
            To: to,
            BeforeOccurredAt: null,
            BeforeId: null,
            Limit: limit);

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
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private sealed class NullOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
