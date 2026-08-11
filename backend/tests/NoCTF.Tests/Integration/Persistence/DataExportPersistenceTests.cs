using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.DataExports;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.DataExports;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.DataExports;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("DataExports")]
[NotInParallel]
public sealed class DataExportPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Competition_archives_are_complete_redacted_audited_and_expire(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_data_exports");
            await postgres.StartAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.Parse("2026-08-06T00:00:00Z");
            var ids = await SeedCompetitionAsync(options, now, cancellationToken);
            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-data-export-tests-{Guid.NewGuid():N}");
            try
            {
                var time = new MutableTimeProvider(now);
                var storage = new LocalObjectStorage(Configuration(storageRoot));
                var outbox = new RecordingOutbox();
                var defaultExportId = Guid.CreateVersion7(now.AddMinutes(1));
                await InsertExportAsync(
                    options,
                    defaultExportId,
                    ids.CompetitionId,
                    ids.AdministratorId,
                    includeProtectedFlags: false,
                    reason: null,
                    now,
                    cancellationToken);

                await using (var db = new NoCtfDbContext(options))
                {
                    var processor = Processor(db, storage, outbox, time);
                    await processor.GenerateAsync(defaultExportId, cancellationToken);
                }

                Guid defaultFileId;
                string defaultObjectKey;
                await using (var verify = new NoCtfDbContext(options))
                {
                    var job = await verify.DataExports.Include(item => item.File).SingleAsync(
                        item => item.Id == defaultExportId,
                        cancellationToken);
                    await Assert.That(job.Status).IsEqualTo(DataExportStatus.Available);
                    await Assert.That(job.ExpiresAt).IsEqualTo(now.AddHours(24));
                    await Assert.That(job.File!.ByteLength).IsGreaterThan(0);
                    defaultFileId = job.File.Id;
                    defaultObjectKey = job.File.ObjectKey;
                    await using var archiveStream = await storage.OpenReadAsync(
                        job.File.ObjectKey,
                        cancellationToken);
                    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                    var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                    await Assert.That(names).Contains("manifest.json");
                    await Assert.That(names).Contains("competition.ndjson");
                    await Assert.That(names).Contains("challenges.ndjson");
                    await Assert.That(names).Contains("teams.ndjson");
                    await Assert.That(names).Contains("gameplay-facts.ndjson");
                    await Assert.That(names).Contains("cheat-incidents.ndjson");
                    await Assert.That(names).Contains("competition-events.ndjson");

                    var challenges = await ReadEntryAsync(
                        archive,
                        "challenges.ndjson",
                        cancellationToken);
                    var gameplayFacts = await ReadEntryAsync(
                        archive,
                        "gameplay-facts.ndjson",
                        cancellationToken);
                    var cheats = await ReadEntryAsync(
                        archive,
                        "cheat-incidents.ndjson",
                        cancellationToken);
                    var manifest = await ReadEntryAsync(
                        archive,
                        "manifest.json",
                        cancellationToken);
                    await Assert.That(challenges).DoesNotContain(ids.ProtectedFlag);
                    await Assert.That(gameplayFacts).DoesNotContain(ids.ProtectedFlag);
                    await Assert.That(challenges).Contains(
                        Convert.ToHexString(SHA256.HashData(
                            Encoding.UTF8.GetBytes(ids.ProtectedFlag))));
                    await Assert.That(gameplayFacts).Contains("ForeignTeamFlagDetected");
                    await Assert.That(cheats).Contains("resolutionHistory");
                    await Assert.That(manifest).Contains("noctf.competition-export/1");
                    await Assert.That(manifest).Contains("competition-events.ndjson");
                }

                var protectedExportId = Guid.CreateVersion7(now.AddMinutes(2));
                await InsertExportAsync(
                    options,
                    protectedExportId,
                    ids.CompetitionId,
                    ids.AdministratorId,
                    includeProtectedFlags: true,
                    reason: "Appeal evidence review",
                    now,
                    cancellationToken);
                await using (var db = new NoCtfDbContext(options))
                {
                    await Processor(db, storage, outbox, time)
                        .GenerateAsync(protectedExportId, cancellationToken);
                }

                await using (var verify = new NoCtfDbContext(options))
                {
                    var job = await verify.DataExports.Include(item => item.File).SingleAsync(
                        item => item.Id == protectedExportId,
                        cancellationToken);
                    await using var archiveStream = await storage.OpenReadAsync(
                        job.File!.ObjectKey,
                        cancellationToken);
                    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                    var challenges = await ReadEntryAsync(
                        archive,
                        "challenges.ndjson",
                        cancellationToken);
                    var gameplayFacts = await ReadEntryAsync(
                        archive,
                        "gameplay-facts.ndjson",
                        cancellationToken);
                    await Assert.That(challenges).Contains(ids.ProtectedFlag);
                    await Assert.That(gameplayFacts).Contains(ids.ProtectedFlag);
                    var audit = await verify.CompetitionEvents.SingleAsync(
                        item => item.Kind
                            == CompetitionEventKind.ProtectedCompetitionExportCreated,
                        cancellationToken);
                    await Assert.That(audit.ActorUserId).IsEqualTo(ids.AdministratorId);
                    await Assert.That(audit.Reason).Contains(protectedExportId.ToString("N"));
                    await Assert.That(audit.Reason).Contains("Appeal evidence review");
                }

                var platformExportId = Guid.CreateVersion7(now.AddMinutes(3));
                await using (var db = new NoCtfDbContext(options))
                {
                    db.DataExports.Add(new DataExport
                    {
                        Id = platformExportId,
                        Scope = DataExportScope.PlatformAudit,
                        RequestedByUserId = ids.AdministratorId,
                        RequestedAt = now,
                        Status = DataExportStatus.Queued,
                        PurgeAt = now.AddDays(30)
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
                await using (var db = new NoCtfDbContext(options))
                {
                    await Processor(db, storage, outbox, time)
                        .GenerateAsync(platformExportId, cancellationToken);
                }
                Guid platformFileId;
                string platformObjectKey;
                await using (var verify = new NoCtfDbContext(options))
                {
                    var job = await verify.DataExports.Include(item => item.File).SingleAsync(
                        item => item.Id == platformExportId,
                        cancellationToken);
                    await Assert.That(job.File!.ContentType).IsEqualTo("application/x-ndjson");
                    platformFileId = job.File.Id;
                    platformObjectKey = job.File.ObjectKey;
                    await using var stream = await storage.OpenReadAsync(
                        job.File.ObjectKey,
                        cancellationToken);
                    using var reader = new StreamReader(stream, Encoding.UTF8, true);
                    var auditExport = await reader.ReadToEndAsync(cancellationToken);
                    await Assert.That(auditExport).Contains("UserAccountLifecycle");
                    await Assert.That(auditExport).Contains("CompetitionEvent");
                }

                time.Advance(TimeSpan.FromHours(25));
                await using (var db = new NoCtfDbContext(options))
                {
                    await Processor(db, storage, outbox, time)
                        .ExpireAsync(defaultExportId, cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    var expired = await verify.DataExports.Include(item => item.File).SingleAsync(
                        item => item.Id == defaultExportId,
                        cancellationToken);
                    await Assert.That(expired.Status).IsEqualTo(DataExportStatus.Expired);
                    await Assert.That(expired.FileId).IsNull();
                    await Assert.That(expired.File).IsNull();
                    await Assert.That(await verify.Files.AnyAsync(
                        item => item.Id == defaultFileId,
                        cancellationToken)).IsTrue();
                }
                await Assert.That(await storage.InspectAsync(
                    defaultObjectKey,
                    cancellationToken)).IsNotNull();
                var expiredCleanup = outbox.Published.OfType<CleanupFile>()
                    .Single(message => message.FileId == defaultFileId);
                await using (var db = new NoCtfDbContext(options))
                {
                    await BackendMessageHandlers.Handle(
                        expiredCleanup,
                        db,
                        storage,
                        cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.Files.AnyAsync(
                        item => item.Id == defaultFileId,
                        cancellationToken)).IsFalse();
                }
                await Assert.That(await storage.InspectAsync(
                    defaultObjectKey,
                    cancellationToken)).IsNull();

                time.Advance(TimeSpan.FromDays(31));
                await using (var db = new NoCtfDbContext(options))
                {
                    await Processor(db, storage, outbox, time)
                        .PurgeAsync(defaultExportId, cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.DataExports.AnyAsync(
                        item => item.Id == defaultExportId,
                        cancellationToken)).IsFalse();
                    await Assert.That(await verify.CompetitionEvents.AnyAsync(
                        item => item.Kind
                            == CompetitionEventKind.ProtectedCompetitionExportCreated,
                        cancellationToken)).IsTrue();
                }

                await using (var db = new NoCtfDbContext(options))
                {
                    await Processor(db, storage, outbox, time)
                        .PurgeAsync(platformExportId, cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.DataExports.AnyAsync(
                        item => item.Id == platformExportId,
                        cancellationToken)).IsFalse();
                    await Assert.That(await verify.Files.AnyAsync(
                        item => item.Id == platformFileId,
                        cancellationToken)).IsTrue();
                }
                await Assert.That(await storage.InspectAsync(
                    platformObjectKey,
                    cancellationToken)).IsNotNull();
                var purgedCleanup = outbox.Published.OfType<CleanupFile>()
                    .Single(message => message.FileId == platformFileId);
                await using (var db = new NoCtfDbContext(options))
                {
                    await BackendMessageHandlers.Handle(
                        purgedCleanup,
                        db,
                        storage,
                        cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.Files.AnyAsync(
                        item => item.Id == platformFileId,
                        cancellationToken)).IsFalse();
                }
                await Assert.That(await storage.InspectAsync(
                    platformObjectKey,
                    cancellationToken)).IsNull();
            }
            finally
            {
                if (Directory.Exists(storageRoot))
                    Directory.Delete(storageRoot, recursive: true);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_requests_share_one_active_job_and_size_failures_are_atomic(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_data_export_lock");
            await postgres.StartAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.Parse("2026-08-06T04:00:00Z");
            var ids = await SeedCompetitionAsync(options, now, cancellationToken);
            var secondCompetitionId = Guid.CreateVersion7(now.AddMinutes(1));
            await using (var db = new NoCtfDbContext(options))
            {
                db.Competitions.Add(new Competition
                {
                    Id = secondCompetitionId,
                    Title = "Second archive subject",
                    OwnerId = ids.OwnerId,
                    Mode = GameMode.Ctf,
                    ConfigurationJson = "{}",
                    ConfigurationUpdatedAt = now,
                    FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    Status = CompetitionStatus.Running,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-data-export-limit-{Guid.NewGuid():N}");
            try
            {
                var command = new RequestDataExportCommand(
                    DataExportScope.CompetitionArchive,
                    ids.CompetitionId,
                    ids.AdministratorId,
                    true,
                    true,
                    false,
                    null);
                async Task<RequestDataExportResult> RequestAsync(Guid competitionId)
                {
                    await using var db = new NoCtfDbContext(options);
                    var store = new DataExportStore(
                        db,
                        new LocalObjectStorage(Configuration(storageRoot)),
                        new RecordingOutbox(),
                        new MutableTimeProvider(now));
                    return await store.RequestAsync(
                        command with { CompetitionId = competitionId },
                        cancellationToken);
                }

                var results = await Task.WhenAll(
                    RequestAsync(ids.CompetitionId),
                    RequestAsync(secondCompetitionId));
                await Assert.That(results.Count(item => item.Failure is null)).IsEqualTo(1);
                await Assert.That(results.Count(item =>
                    item.Failure == RequestDataExportFailure.ActiveExportExists)).IsEqualTo(1);
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.DataExports.CountAsync(cancellationToken))
                        .IsEqualTo(1);
                }

                Guid exportId;
                await using (var db = new NoCtfDbContext(options))
                {
                    var job = await db.DataExports.SingleAsync(cancellationToken);
                    exportId = job.Id;
                    job.Status = DataExportStatus.Queued;
                    await db.SaveChangesAsync(cancellationToken);
                }
                var tinyConfiguration = Configuration(storageRoot, maximumBytes: 128);
                var storage = new LocalObjectStorage(tinyConfiguration);
                await using (var db = new NoCtfDbContext(options))
                {
                    var processor = new DataExportProcessor(
                        db,
                        storage,
                        new RecordingOutbox(),
                        new CompetitionEventStore(db, new RecordingOutbox()),
                        new PlatformAuditLogStore(db),
                        tinyConfiguration,
                        new MutableTimeProvider(now),
                        NullLogger<DataExportProcessor>.Instance);
                    await processor.GenerateAsync(exportId, cancellationToken);
                }
                await using (var verify = new NoCtfDbContext(options))
                {
                    var failed = await verify.DataExports.SingleAsync(cancellationToken);
                    await Assert.That(failed.Status).IsEqualTo(DataExportStatus.Failed);
                    await Assert.That(failed.FailureCode)
                        .IsEqualTo(DataExportFailureCode.SizeLimitExceeded);
                    await Assert.That(failed.FileId).IsNull();
                }
            }
            finally
            {
                if (Directory.Exists(storageRoot))
                    Directory.Delete(storageRoot, recursive: true);
            }
        });
    }

    private static DataExportProcessor Processor(
        NoCtfDbContext db,
        LocalObjectStorage storage,
        RecordingOutbox outbox,
        TimeProvider time) =>
        new(
            db,
            storage,
            outbox,
            new CompetitionEventStore(db, outbox),
            new PlatformAuditLogStore(db),
            Configuration("unused"),
            time,
            NullLogger<DataExportProcessor>.Instance);

    private static IConfiguration Configuration(
        string storageRoot,
        long? maximumBytes = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Storage:LocalRoot"] = storageRoot
        };
        if (maximumBytes is not null)
            values["DataExports:MaxArchiveBytes"] = maximumBytes.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static async Task<string> ReadEntryAsync(
        ZipArchive archive,
        string name,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(name);
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task InsertExportAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid exportId,
        Guid competitionId,
        Guid administratorId,
        bool includeProtectedFlags,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        db.DataExports.Add(new DataExport
        {
            Id = exportId,
            Scope = DataExportScope.CompetitionArchive,
            CompetitionId = competitionId,
            RequestedByUserId = administratorId,
            RequestedAt = now,
            IncludeProtectedFlags = includeProtectedFlags,
            Reason = reason,
            Status = DataExportStatus.Queued,
            PurgeAt = now.AddDays(30)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<TestIds> SeedCompetitionAsync(
        DbContextOptions<NoCtfDbContext> options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ids = new TestIds(
            Guid.CreateVersion7(now),
            Guid.CreateVersion7(now.AddMilliseconds(1)),
            Guid.CreateVersion7(now.AddMilliseconds(2)),
            Guid.CreateVersion7(now.AddMilliseconds(3)),
            Guid.CreateVersion7(now.AddMilliseconds(4)),
            Guid.CreateVersion7(now.AddMilliseconds(5)),
            Guid.CreateVersion7(now.AddMilliseconds(6)),
            Guid.CreateVersion7(now.AddMilliseconds(7)),
            Guid.CreateVersion7(now.AddMilliseconds(8)),
            Guid.CreateVersion7(now.AddMilliseconds(9)),
            "flag{complete-archive-secret}");
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.AddRange(
            User(ids.AdministratorId, "export-admin", UserRole.Administrator, now),
            User(ids.OwnerId, "export-owner", UserRole.Organizer, now),
            User(ids.MemberId, "export-member", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = ids.CompetitionId,
            Title = "Archive evidence",
            OwnerId = ids.OwnerId,
            ManagerIds = [ids.AdministratorId],
            Mode = GameMode.Ctf,
            ConfigurationJson = "{}",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Challenges.Add(new Challenge
        {
            Id = ids.ChallengeId,
            OwnerId = ids.OwnerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Shared,
            Title = "Archived challenge",
            Direction = "Web",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = ids.CompetitionChallengeId,
            CompetitionId = ids.CompetitionId,
            ChallengeId = ids.ChallengeId,
            BaseScore = 500,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now,
            DeletedAt = now.AddMinutes(10),
            Hints =
            [
                new CompetitionChallengeHint
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(11)),
                    Content = "Historical hint",
                    Cost = 10,
                    PublishedAt = now,
                    HiddenAt = now.AddMinutes(9)
                }
            ]
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = ids.ChallengeFlagId,
            CompetitionChallengeId = ids.CompetitionChallengeId,
            Flag = ids.ProtectedFlag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(ids.ProtectedFlag)),
            CreatedAt = now,
            DeletedAt = now.AddMinutes(8)
        });
        db.Teams.AddRange(
            Team(ids.SourceTeamId, ids.CompetitionId, "Source", ids.MemberId, now),
            Team(ids.OwnerTeamId, ids.CompetitionId, "Owner", ids.OwnerId, now));
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = ids.GameplayFactId,
            CompetitionId = ids.CompetitionId,
            TeamId = ids.SourceTeamId,
            CompetitionChallengeId = ids.CompetitionChallengeId,
            ActorUserId = ids.MemberId,
            Kind = GameplayFactKind.FlagAttempt,
            OccurredAt = now,
            Value = ids.ProtectedFlag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(ids.ProtectedFlag)),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Rejected,
            FailureCode = GameplayFactFailureCode.ForeignTeamFlagDetected,
            VictimTeamId = ids.OwnerTeamId,
            UpdatedAt = now
        });
        db.CompetitionEvents.AddRange(
            new CompetitionEvent
            {
                Id = Guid.CreateVersion7(now.AddMilliseconds(10)),
                CompetitionId = ids.CompetitionId,
                Kind = CompetitionEventKind.CompetitionLifecycleChanged,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Public,
                ActorUserId = ids.OwnerId,
                SubjectType = EntityReferenceKind.Competition,
                SubjectId = ids.CompetitionId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    competitionStatus = CompetitionStatus.Running
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                OccurredAt = now
            },
            new CompetitionEvent
            {
                Id = Guid.CreateVersion7(now.AddMilliseconds(12)),
                CompetitionId = ids.CompetitionId,
                Kind = CompetitionEventKind.CheatIncidentDetected,
                Level = CompetitionEventLevel.Warning,
                Visibility = CompetitionEventVisibility.Staff,
                ActorUserId = ids.MemberId,
                SubjectType = EntityReferenceKind.GameplayFact,
                SubjectId = ids.GameplayFactId,
                RelatedType = EntityReferenceKind.Team,
                RelatedId = ids.SourceTeamId,
                OccurredAt = now
            },
            new CompetitionEvent
            {
                Id = Guid.CreateVersion7(now.AddMilliseconds(13)),
                CompetitionId = ids.CompetitionId,
                Kind = CompetitionEventKind.CheatIncidentDismissed,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Staff,
                ActorUserId = ids.AdministratorId,
                SubjectType = EntityReferenceKind.GameplayFact,
                SubjectId = ids.GameplayFactId,
                RelatedType = EntityReferenceKind.Team,
                RelatedId = ids.SourceTeamId,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    reason = "False positive"
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                OccurredAt = now.AddMinutes(1)
            });
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(now.AddMilliseconds(14)),
            SourceType = NotificationSourceType.User,
            SourceId = ids.AdministratorId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            Kind = NotificationKind.UserAccountLifecycleChanged,
            ContentJson = JsonSerializer.Serialize(new UserAccountLifecycleFact(
                1,
                ids.MemberId,
                "export-member",
                UserAccountLifecycleAction.Disabled,
                "Historical account action",
                false), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RelatedType = EntityReferenceKind.User,
            RelatedId = ids.MemberId,
            SentAt = now.AddMinutes(2)
        });
        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private static User User(
        Guid id,
        string name,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Team Team(
        Guid id,
        Guid competitionId,
        string name,
        Guid captainId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CaptainId = captainId,
            MemberIds = [captainId],
            InvitationToken = id.ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        };

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> OptionsFor(
        PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed record TestIds(
        Guid AdministratorId,
        Guid OwnerId,
        Guid MemberId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid ChallengeFlagId,
        Guid SourceTeamId,
        Guid OwnerTeamId,
        Guid GameplayFactId,
        string ProtectedFlag);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
