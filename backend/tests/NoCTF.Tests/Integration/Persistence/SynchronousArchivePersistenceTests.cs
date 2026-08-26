using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Exports;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Exports;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("SynchronousArchives")]
[NotInParallel]
public sealed class SynchronousArchivePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Competition_archive_is_streamed_redacted_and_audited(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_sync_competition_archive");
            await postgres.StartAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
            var ids = await SeedAsync(options, now, cancellationToken);
            var temporaryDirectory = TemporaryDirectory();
            try
            {
                await using var db = new NoCtfDbContext(options);
                var generator = Generator(db, temporaryDirectory, TimeProvider.System);
                var useCase = new ExportCompetitionArchive(generator);

                var redacted = await useCase.ExecuteAsync(new(
                    ids.CompetitionId,
                    ids.OwnerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);

                await Assert.That(redacted.Failure).IsNull();
                await Assert.That(redacted.Archive).IsNotNull();
                await using (redacted.Archive!.Content)
                {
                    using var archive = new ZipArchive(
                        redacted.Archive.Content,
                        ZipArchiveMode.Read,
                        leaveOpen: true);
                    var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                    await Assert.That(names).Contains("manifest.json");
                    await Assert.That(names).Contains("competition.ndjson");
                    await Assert.That(names).Contains("challenge-flags.ndjson");
                    var flags = await ReadEntryAsync(
                        archive,
                        "challenge-flags.ndjson",
                        cancellationToken);
                    await Assert.That(flags).DoesNotContain(ids.ProtectedFlag);
                    await Assert.That(flags).Contains(Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(ids.ProtectedFlag))));
                }

                var protectedArchive = await useCase.ExecuteAsync(new(
                    ids.CompetitionId,
                    ids.AdministratorId,
                    RequesterIsAdministrator: true,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: true,
                    Reason: "Investigate the reported scoring incident."), cancellationToken);
                await Assert.That(protectedArchive.Failure).IsNull();
                await using (protectedArchive.Archive!.Content)
                {
                    using var archive = new ZipArchive(
                        protectedArchive.Archive.Content,
                        ZipArchiveMode.Read,
                        leaveOpen: true);
                    var flags = await ReadEntryAsync(
                        archive,
                        "challenge-flags.ndjson",
                        cancellationToken);
                    await Assert.That(flags).Contains(ids.ProtectedFlag);
                }

                await using var verify = new NoCtfDbContext(options);
                var auditEvents = await verify.CompetitionEvents
                    .Where(item => item.CompetitionId == ids.CompetitionId
                        && item.Kind == CompetitionEventKind.CompetitionArchiveExported)
                    .OrderBy(item => item.OccurredAt)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(auditEvents).Count().IsEqualTo(2);
                await Assert.That(auditEvents[0].PayloadJson).DoesNotContain(ids.ProtectedFlag);
                await Assert.That(auditEvents[1].PayloadJson)
                    .Contains("Investigate the reported scoring incident.");
                await Assert.That(Directory.GetFiles(temporaryDirectory)).IsEmpty();
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Access_and_limits_fail_without_persistent_or_file_residue(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_sync_archive_failures");
            await postgres.StartAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
            var ids = await SeedAsync(options, now, cancellationToken);
            var temporaryDirectory = TemporaryDirectory();
            try
            {
                await using var db = new NoCtfDbContext(options);
                var generator = Generator(
                    db,
                    temporaryDirectory,
                    TimeProvider.System,
                    new Dictionary<string, string?>
                    {
                        ["SynchronousExports:MaxRecords"] = "1"
                    });

                var forbidden = await generator.GenerateCompetitionAsync(new(
                    ids.CompetitionId,
                    ids.StrangerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);
                var notFound = await generator.GenerateCompetitionAsync(new(
                    Guid.NewGuid(),
                    ids.OwnerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);
                var limited = await generator.GenerateCompetitionAsync(new(
                    ids.CompetitionId,
                    ids.OwnerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);

                await using var compressedDb = new NoCtfDbContext(options);
                var compressedGenerator = Generator(
                    compressedDb,
                    temporaryDirectory,
                    TimeProvider.System,
                    new Dictionary<string, string?>
                    {
                        ["SynchronousExports:MaxCompressedBytes"] = "64"
                    });
                var compressedLimited = await compressedGenerator.GenerateCompetitionAsync(new(
                    ids.CompetitionId,
                    ids.OwnerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);

                await using var memoryDb = new NoCtfDbContext(options);
                var memoryGenerator = Generator(
                    memoryDb,
                    temporaryDirectory,
                    TimeProvider.System,
                    new Dictionary<string, string?>
                    {
                        ["SynchronousExports:MaxWorkingSetBytes"] = "1"
                    });
                var memoryLimited = await memoryGenerator.GenerateCompetitionAsync(new(
                    ids.CompetitionId,
                    ids.OwnerId,
                    RequesterIsAdministrator: false,
                    RequesterIsHuman: true,
                    IncludeProtectedFlags: false,
                    Reason: null), cancellationToken);

                await using var deadlineDb = new NoCtfDbContext(options);
                var deadlineClock = new FakeTimeProvider(now);
                var deadlineGenerator = Generator(
                    deadlineDb,
                    temporaryDirectory,
                    deadlineClock,
                    new Dictionary<string, string?>
                    {
                        ["SynchronousExports:MaxDurationSeconds"] = "1"
                    });
                var deadlineTask = deadlineGenerator.GeneratePlatformAuditAsync(new(
                    ids.AdministratorId,
                    RequesterIsAdministrator: true,
                    RequesterIsHuman: true,
                    Kind: null,
                    CompetitionId: null,
                    ActorId: null,
                    From: null,
                    To: null), cancellationToken);
                deadlineClock.Advance(TimeSpan.FromSeconds(2));
                var deadlineLimited = await deadlineTask;

                using var cancelled = new CancellationTokenSource();
                await cancelled.CancelAsync();
                Func<Task> cancelledExport = async () =>
                {
                    await generator.GenerateCompetitionAsync(new(
                        ids.CompetitionId,
                        ids.OwnerId,
                        RequesterIsAdministrator: false,
                        RequesterIsHuman: true,
                        IncludeProtectedFlags: false,
                        Reason: null), cancelled.Token);
                };

                await Assert.That(forbidden.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.Forbidden);
                await Assert.That(notFound.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.SubjectNotFound);
                await Assert.That(limited.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.RecordLimitExceeded);
                await Assert.That(compressedLimited.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.CompressedSizeLimitExceeded);
                await Assert.That(memoryLimited.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.MemoryLimitExceeded);
                await Assert.That(deadlineLimited.Failure)
                    .IsEqualTo(SynchronousArchiveFailure.TimeLimitExceeded);
                await Assert.That(cancelledExport).Throws<OperationCanceledException>();
                await using var verify = new NoCtfDbContext(options);
                await Assert.That(await verify.CompetitionEvents.AnyAsync(item =>
                    item.Kind == CompetitionEventKind.CompetitionArchiveExported,
                    cancellationToken)).IsFalse();
                await Assert.That(await verify.Notifications.AnyAsync(item =>
                    item.Kind == NotificationKind.PlatformAuditExported,
                    cancellationToken)).IsFalse();
                await Assert.That(Directory.GetFiles(temporaryDirectory)).IsEmpty();
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Platform_audit_archive_honors_filters_and_records_success(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = CreatePostgres("noctf_sync_platform_audit");
            await postgres.StartAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
            var ids = await SeedAsync(options, now, cancellationToken);
            var temporaryDirectory = TemporaryDirectory();
            try
            {
                await using var db = new NoCtfDbContext(options);
                var generator = Generator(db, temporaryDirectory, TimeProvider.System);
                var useCase = new ExportPlatformAuditArchive(generator);

                var result = await useCase.ExecuteAsync(new(
                    ids.AdministratorId,
                    RequesterIsAdministrator: true,
                    RequesterIsHuman: true,
                    Kind: PlatformAuditKind.CompetitionEvent,
                    CompetitionId: ids.CompetitionId,
                    ActorId: ids.OwnerId,
                    From: now.AddMinutes(-1),
                    To: now.AddMinutes(1)), cancellationToken);

                await Assert.That(result.Failure).IsNull();
                await using (result.Archive!.Content)
                {
                    using var archive = new ZipArchive(
                        result.Archive.Content,
                        ZipArchiveMode.Read,
                        leaveOpen: true);
                    var audit = await ReadEntryAsync(
                        archive,
                        "audit.ndjson",
                        cancellationToken);
                    await Assert.That(audit).Contains(ids.CompetitionId.ToString());
                    await Assert.That(audit).Contains("CompetitionEvent");
                }

                await using var verify = new NoCtfDbContext(options);
                var auditFact = await verify.Notifications.SingleAsync(item =>
                    item.Kind == NotificationKind.PlatformAuditExported,
                    cancellationToken);
                await Assert.That(auditFact.SourceId).IsEqualTo(ids.AdministratorId);
                await Assert.That(auditFact.ContentJson).Contains("recordCount");
                await Assert.That(auditFact.ContentJson).DoesNotContain(ids.ProtectedFlag);
                await Assert.That(Directory.GetFiles(temporaryDirectory)).IsEmpty();
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
        });
    }

    private static PostgresSynchronousArchiveGenerator Generator(
        NoCtfDbContext db,
        string temporaryDirectory,
        TimeProvider timeProvider,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["SynchronousExports:TemporaryDirectory"] = temporaryDirectory,
            ["SynchronousExports:MaxRecords"] = "10000",
            ["SynchronousExports:MaxCompressedBytes"] = "10485760",
            ["SynchronousExports:MaxWorkingSetBytes"] = "1048576",
            ["SynchronousExports:MaxDurationSeconds"] = "30"
        };
        if (overrides is not null)
        {
            foreach (var item in overrides)
                values[item.Key] = item.Value;
        }
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new(
            db,
            new PlatformAuditLogStore(db),
            configuration,
            timeProvider,
            NullLogger<PostgresSynchronousArchiveGenerator>.Instance);
    }

    private static async Task<TestIds> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ids = new TestIds(
            Guid.CreateVersion7(now),
            Guid.CreateVersion7(now.AddTicks(1)),
            Guid.CreateVersion7(now.AddTicks(2)),
            Guid.CreateVersion7(now.AddTicks(3)),
            Guid.CreateVersion7(now.AddTicks(4)),
            Guid.CreateVersion7(now.AddTicks(5)),
            Guid.CreateVersion7(now.AddTicks(6)),
            "flag{streamed-secret}");
        await using var db = new NoCtfDbContext(options);
        // Stage 10 owns the single EF baseline regeneration. Until then the
        // integration fixture creates the current relational model directly.
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.AddRange(
            User(ids.AdministratorId, "archive-admin", UserRole.Administrator, now),
            User(ids.OwnerId, "archive-owner", UserRole.User, now),
            User(ids.StrangerId, "archive-stranger", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = ids.CompetitionId,
            Title = "Archive competition",
            Description = "Archive integration fixture",
            OwnerId = ids.OwnerId,
            Mode = GameMode.Ctf,
            ConfigurationJson = "{}",
            FlagDerivationSecret = Enumerable.Repeat((byte)0x2A, 32).ToArray(),
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = ids.ChallengeId,
            OwnerId = ids.OwnerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = "Archive challenge",
            Direction = "Web",
            DefinitionJson = "{\"schemaVersion\":2}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = ids.CompetitionChallengeId,
            CompetitionId = ids.CompetitionId,
            ChallengeId = ids.ChallengeId,
            Order = 1,
            IsPublished = true,
            RulesJson = "{}",
            UpdatedAt = now
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = ids.ChallengeFlagId,
            CompetitionChallengeId = ids.CompetitionChallengeId,
            Flag = ids.ProtectedFlag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(ids.ProtectedFlag)),
            MatchKind = ChallengeFlagMatchKind.Exact,
            CreatedAt = now
        });
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = Guid.CreateVersion7(now.AddTicks(7)),
            CompetitionId = ids.CompetitionId,
            Kind = CompetitionEventKind.CompetitionCreated,
            Level = CompetitionEventLevel.Information,
            Visibility = CompetitionEventVisibility.Staff,
            ActorUserId = ids.OwnerId,
            SubjectType = EntityReferenceKind.Competition,
            SubjectId = ids.CompetitionId,
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    private static User User(
        Guid id,
        string name,
        UserRole role,
        DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = role,
        AccountStatus = UserAccountStatus.Active,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<string> ReadEntryAsync(
        ZipArchive archive,
        string name,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(
            $"Archive entry {name} was not generated.");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string TemporaryDirectory() => Path.Combine(
        Path.GetTempPath(),
        $"noctf-synchronous-archives-{Guid.NewGuid():N}");

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
        Guid StrangerId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid ChallengeFlagId,
        string ProtectedFlag);
}
