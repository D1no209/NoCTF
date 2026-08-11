using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.DataExports;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionHardDeletePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Hard_delete_preview_preserves_history_and_allows_only_an_unreferenced_competition(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_hard_delete")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var emptyCompetitionId = Guid.CreateVersion7(now.AddTicks(1));
            var historicalCompetitionId = Guid.CreateVersion7(now.AddTicks(2));
            var posterFileId = Guid.CreateVersion7(now.AddTicks(3));
            var eventId = Guid.CreateVersion7(now.AddTicks(4));
            var notificationId = Guid.CreateVersion7(now.AddTicks(5));
            var dataExportId = Guid.CreateVersion7(now.AddTicks(6));
            var challengeId = Guid.CreateVersion7(now.AddTicks(7));
            var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(8));
            var teamId = Guid.CreateVersion7(now.AddTicks(9));
            var gameplayFactId = Guid.CreateVersion7(now.AddTicks(10));
            var runtimeId = Guid.CreateVersion7(now.AddTicks(11));
            var patchFileId = Guid.CreateVersion7(now.AddTicks(12));
            var patchUploadId = Guid.CreateVersion7(now.AddTicks(13));

            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.Add(User(ownerId, now));
                setup.Files.AddRange(
                    File(posterFileId, "poster.png", "image/png", now),
                    File(patchFileId, "fix.zip", "application/zip", now));
                setup.Competitions.AddRange(
                    Competition(emptyCompetitionId, ownerId, "Empty draft", now),
                    Competition(
                        historicalCompetitionId,
                        ownerId,
                        "Historical draft",
                        now,
                        deletedAt: now,
                        posterFileId));
                setup.Challenges.Add(new Challenge
                {
                    Id = challengeId,
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Visibility = ChallengeVisibility.Private,
                    Title = "Historical challenge",
                    Direction = "Web",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new CompetitionChallenge
                {
                    Id = competitionChallengeId,
                    CompetitionId = historicalCompetitionId,
                    ChallengeId = challengeId,
                    BaseScore = 500,
                    IsPublished = true,
                    UpdatedAt = now
                });
                setup.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = historicalCompetitionId,
                    Name = "Historical team",
                    NormalizedName = "HISTORICAL TEAM",
                    CaptainId = ownerId,
                    MemberIds = [ownerId],
                    InvitationToken = "0123456789abcdefghijklmnopqrstuv",
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                setup.GameplayFacts.Add(new GameplayFact
                {
                    Id = gameplayFactId,
                    CompetitionId = historicalCompetitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    ActorUserId = ownerId,
                    Kind = GameplayFactKind.FlagAttempt,
                    OccurredAt = now,
                    Value = "flag{historical}",
                    ValueSha256 = new byte[32],
                    State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Correct,
                    UpdatedAt = now
                });
                setup.RuntimeInstances.Add(new RuntimeInstance
                {
                    Id = runtimeId,
                    CompetitionId = historicalCompetitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    Generation = 1,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    RunnerPool = "test",
                    State = RuntimeState.Stopped,
                    CreatedAt = now,
                    StoppedAt = now
                });
                setup.PatchUploads.Add(new PatchUpload
                {
                    Id = patchUploadId,
                    CompetitionId = historicalCompetitionId,
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = teamId,
                    UploadedByUserId = ownerId,
                    FileId = patchFileId,
                    UploadedAt = now
                });
                setup.CompetitionEvents.Add(new CompetitionEvent
                {
                    Id = eventId,
                    CompetitionId = historicalCompetitionId,
                    Kind = CompetitionEventKind.CompetitionCreated,
                    Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Staff,
                    ActorUserId = ownerId,
                    SubjectType = EntityReferenceKind.Competition,
                    SubjectId = historicalCompetitionId,
                    OccurredAt = now
                });
                setup.Notifications.Add(new Notification
                {
                    Id = notificationId,
                    SourceType = NotificationSourceType.System,
                    TargetType = NotificationTargetType.User,
                    TargetId = ownerId,
                    Kind = NotificationKind.CompetitionLifecycleChanged,
                    ContentJson = "{\"schemaVersion\":1}",
                    RelatedType = EntityReferenceKind.Competition,
                    RelatedId = historicalCompetitionId,
                    SentAt = now
                });
                setup.DataExports.Add(new DataExport
                {
                    Id = dataExportId,
                    Scope = DataExportScope.CompetitionArchive,
                    CompetitionId = historicalCompetitionId,
                    RequestedByUserId = ownerId,
                    RequestedAt = now,
                    Status = DataExportStatus.Failed,
                    PurgeAt = now.AddDays(1),
                    FailureCode = DataExportFailureCode.GenerationFailed
                });
                await setup.SaveChangesAsync(ct);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var store = new AdminCompetitionStore(db);
                var emptyPreview = await store.PreviewHardDeleteAsync(
                    emptyCompetitionId,
                    ownerId,
                    false,
                    ct);
                var historicalPreview = await store.PreviewHardDeleteAsync(
                    historicalCompetitionId,
                    ownerId,
                    false,
                    ct);

                await Assert.That(emptyPreview).IsNotNull();
                await Assert.That(emptyPreview!.IsSoftDeleted).IsFalse();
                await Assert.That(emptyPreview.CanHardDelete).IsTrue();
                await Assert.That(emptyPreview.References).IsEmpty();
                await Assert.That(historicalPreview).IsNotNull();
                await Assert.That(historicalPreview!.IsSoftDeleted).IsTrue();
                await Assert.That(historicalPreview.CanHardDelete).IsFalse();
                await Assert.That(historicalPreview.References)
                    .IsEquivalentTo(
                    [
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.HistoricalEvent,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.Team,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.CompetitionChallenge,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.GameplayFact,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.RuntimeInstance,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.PatchUpload,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.DataExport,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.Notification,
                            1),
                        new CompetitionHardDeleteReference(
                            CompetitionHardDeleteReferenceKind.PosterFile,
                            1)
                    ]);

                var blocked = await store.HardDeleteAsync(
                    historicalCompetitionId,
                    ownerId,
                    false,
                    ct);
                var deleted = await store.HardDeleteAsync(
                    emptyCompetitionId,
                    ownerId,
                    false,
                    ct);
                await Assert.That(blocked.State)
                    .IsEqualTo(CompetitionHardDeleteState.Blocked);
                await Assert.That(blocked.Preview!.References.Any(reference =>
                    reference.Kind == CompetitionHardDeleteReferenceKind.HistoricalEvent))
                    .IsTrue();
                await Assert.That(deleted.State)
                    .IsEqualTo(CompetitionHardDeleteState.Deleted);
            }

            await using (var verification = new NoCtfDbContext(options))
            {
                await Assert.That(await verification.Competitions.IgnoreQueryFilters().AnyAsync(
                    item => item.Id == emptyCompetitionId,
                    ct)).IsFalse();
                await Assert.That(await verification.Competitions.IgnoreQueryFilters().AnyAsync(
                    item => item.Id == historicalCompetitionId,
                    ct)).IsTrue();
                await Assert.That(await verification.CompetitionEvents.AnyAsync(
                    item => item.Id == eventId,
                    ct)).IsTrue();
                await Assert.That(await verification.Notifications.AnyAsync(
                    item => item.Id == notificationId,
                    ct)).IsTrue();
                await Assert.That(await verification.DataExports.AnyAsync(
                    item => item.Id == dataExportId,
                    ct)).IsTrue();
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_committed_event_is_observed_before_hard_delete_decides(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_hard_delete_event_race")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now.AddTicks(1));
            var eventId = Guid.CreateVersion7(now.AddTicks(2));
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(ct);
                setup.Users.Add(User(ownerId, now));
                setup.Competitions.Add(Competition(
                    competitionId,
                    ownerId,
                    "Concurrent event",
                    now));
                await setup.SaveChangesAsync(ct);
            }

            await using var eventDb = new NoCtfDbContext(options);
            await using var eventTransaction = await eventDb.Database.BeginTransactionAsync(ct);
            eventDb.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = eventId,
                CompetitionId = competitionId,
                Kind = CompetitionEventKind.CompetitionCreated,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Staff,
                ActorUserId = ownerId,
                SubjectType = EntityReferenceKind.Competition,
                SubjectId = competitionId,
                OccurredAt = now
            });
            await eventDb.SaveChangesAsync(ct);

            await using var deleteDb = new NoCtfDbContext(options);
            var deleteTask = new AdminCompetitionStore(deleteDb).HardDeleteAsync(
                competitionId,
                ownerId,
                false,
                ct);
            await Task.Delay(250, ct);
            await Assert.That(deleteTask.IsCompleted).IsFalse();
            await eventTransaction.CommitAsync(ct);

            var result = await deleteTask;
            await Assert.That(result.State)
                .IsEqualTo(CompetitionHardDeleteState.Blocked);
            await Assert.That(result.Preview!.References.Any(reference =>
                reference.Kind == CompetitionHardDeleteReferenceKind.HistoricalEvent
                && reference.Count == 1)).IsTrue();
            await using var verification = new NoCtfDbContext(options);
            await Assert.That(await verification.Competitions.IgnoreQueryFilters().AnyAsync(
                item => item.Id == competitionId,
                ct)).IsTrue();
            await Assert.That(await verification.CompetitionEvents.AnyAsync(
                item => item.Id == eventId,
                ct)).IsTrue();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Normal_create_delete_restore_history_permanently_blocks_hard_delete(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_hard_delete_normal_flow")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            db.Users.Add(User(ownerId, now));
            await db.SaveChangesAsync(ct);
            var outbox = new RecordingOutbox();
            var eventStore = new CompetitionEventStore(db, outbox);
            var management = new CompetitionManagementStore(
                db,
                outbox,
                eventStore);

            var created = await management.CreateAsync(new(
                "Normal competition",
                null,
                GameMode.Ctf,
                now.AddHours(1),
                now.AddHours(2),
                true,
                5,
                1,
                ownerId,
                now), ct);
            var competitionId = created.Competition!.Id;
            var administration = new AdminCompetitionStore(db);
            var createdPreview = await administration.PreviewHardDeleteAsync(
                competitionId,
                ownerId,
                false,
                ct);
            await Assert.That(createdPreview!.References.Single(reference =>
                reference.Kind == CompetitionHardDeleteReferenceKind.HistoricalEvent).Count)
                .IsEqualTo(1);

            await Assert.That(await management.SoftDeleteAsync(
                competitionId,
                CompetitionStatus.Draft,
                ownerId,
                now.AddMinutes(1),
                ct)).IsTrue();
            db.ChangeTracker.Clear();
            await Assert.That(await db.CompetitionEvents.CountAsync(
                item => item.CompetitionId == competitionId,
                ct)).IsEqualTo(2);
            var restored = await administration.RestoreAsync(
                competitionId,
                ownerId,
                false,
                now.AddMinutes(2),
                ct);
            await Assert.That(restored.State).IsEqualTo(CompetitionRestoreState.Restored);

            var blocked = await administration.HardDeleteAsync(
                competitionId,
                ownerId,
                false,
                ct);
            await Assert.That(blocked.State)
                .IsEqualTo(CompetitionHardDeleteState.Blocked);
            await Assert.That(blocked.Preview!.IsSoftDeleted).IsFalse();
            await Assert.That(blocked.Preview.References.Single(reference =>
                reference.Kind == CompetitionHardDeleteReferenceKind.HistoricalEvent).Count)
                .IsEqualTo(2);
            await Assert.That(await db.Competitions.IgnoreQueryFilters().AnyAsync(
                item => item.Id == competitionId,
                ct)).IsTrue();
            await Assert.That(await db.CompetitionEvents.CountAsync(
                item => item.CompetitionId == competitionId,
                ct)).IsEqualTo(2);
        });
    }

    private static User User(Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = "competition-owner",
            NormalizedUserName = "COMPETITION-OWNER",
            Email = "competition-owner@example.test",
            NormalizedEmail = "COMPETITION-OWNER@EXAMPLE.TEST",
            PasswordHash = "password-hash",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static StoredFile File(
        Guid id,
        string fileName,
        string contentType,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            ObjectKey = $"hard-delete-tests/{id:N}",
            FileName = fileName,
            ContentType = contentType,
            ByteLength = 1,
            Sha256 = new byte[32],
            CreatedAt = now
        };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now,
        DateTimeOffset? deletedAt = null,
        Guid? posterFileId = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            PosterFileId = posterFileId,
            Mode = GameMode.Ctf,
            ConfigurationJson = "{\"schemaVersion\":1}",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = deletedAt
        };

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
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
}
