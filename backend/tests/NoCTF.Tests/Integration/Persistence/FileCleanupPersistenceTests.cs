using System.Text;
using System.Security.Cryptography;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("Storage")]
[NotInParallel]
public sealed class FileCleanupPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Registration_persists_a_delayed_cleanup_lease_and_abandonment_accelerates_it(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_file_registry")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
            var fileId = Guid.CreateVersion7(now);
            var outbox = new RecordingOutbox();

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                var registry = new ManagedFileUploadRegistry(
                    db,
                    outbox,
                    NullLogger<ManagedFileUploadRegistry>.Instance);
                await registry.RegisterAsync(
                    new ManagedFileUpload(
                        fileId,
                        "attachments/leased",
                        "leased.txt",
                        "text/plain",
                        5,
                        new string('0', 64)),
                    now,
                    cancellationToken);

                await Assert.That(await db.Files.AnyAsync(
                    candidate => candidate.Id == fileId,
                    cancellationToken)).IsTrue();
                var scheduled = outbox.Scheduled.Single();
                await Assert.That(scheduled.Message).IsEqualTo(new CleanupFile(fileId));
                await Assert.That(scheduled.At).IsEqualTo(now.AddHours(24));

                await registry.AbandonAsync(fileId, cancellationToken);
                await Assert.That(outbox.Published.Single())
                    .IsEqualTo(new CleanupFile(fileId));
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Cleanup_preserves_shared_references_and_retries_storage_failures(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_file_cleanup")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-file-cleanup-tests-{Guid.NewGuid():N}");

            try
            {
                using var storage = StorageFactory.Disk(storageRoot);
                var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
                var fileId = Guid.CreateVersion7(now);
                var userId = Guid.CreateVersion7(now.AddMilliseconds(1));
                const string objectKey = "shared/platform-identity.png";
                await using var content = new MemoryStream(Encoding.UTF8.GetBytes("shared-file"));
                await storage.SetObject(
                    objectKey,
                    content,
                    "image/png",
                    cancellationToken: cancellationToken);
                var contentBytes = Encoding.UTF8.GetBytes("shared-file");

                await using (var db = new NoCtfDbContext(options))
                {
                    await db.Database.EnsureCreatedAsync(cancellationToken);
                    db.Files.Add(new StoredFile
                    {
                        Id = fileId,
                        ObjectKey = objectKey,
                        FileName = "identity.png",
                        ContentType = "image/png",
                        ByteLength = contentBytes.LongLength,
                        Sha256 = SHA256.HashData(contentBytes),
                        CreatedAt = now
                    });
                    db.Users.Add(new User
                    {
                        Id = userId,
                        UserName = "shared-file-user",
                        NormalizedUserName = "SHARED-FILE-USER",
                        Email = "shared-file@example.test",
                        PasswordHash = "test",
                        AvatarFileId = fileId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    var settings = await db.PlatformSettings.SingleAsync(cancellationToken);
                    settings.LogoFileId = fileId;
                    await db.SaveChangesAsync(cancellationToken);
                }

                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await AssertFileExistsAsync(
                    options,
                    storage,
                    fileId,
                    objectKey,
                    cancellationToken);

                await using (var db = new NoCtfDbContext(options))
                {
                    var user = await db.Users.SingleAsync(
                        candidate => candidate.Id == userId,
                        cancellationToken);
                    user.AvatarFileId = null;
                    await db.SaveChangesAsync(cancellationToken);
                }
                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await AssertFileExistsAsync(
                    options,
                    storage,
                    fileId,
                    objectKey,
                    cancellationToken);

                await using (var db = new NoCtfDbContext(options))
                {
                    var settings = await db.PlatformSettings.SingleAsync(cancellationToken);
                    settings.LogoFileId = null;
                    await db.SaveChangesAsync(cancellationToken);
                }

                var failOnce = new FailOnceDeleteStore(storage);
                var competitionId = Guid.NewGuid();
                var teamId = Guid.NewGuid();
                await using (var db = new NoCtfDbContext(options))
                {
                    db.Competitions.Add(new Competition
                    {
                        Id = competitionId, OwnerId = userId, Title = "Soft-deleted file owner",
                        Mode = GameMode.Ctf, ConfigurationJson = "{\"schemaVersion\":1}", FlagDerivationSecret = new byte[32],
                        StartAt = now, EndAt = now.AddHours(1), Status = CompetitionStatus.Finished,
                        CreatedAt = now, UpdatedAt = now, DeletedAt = now, PosterFileId = fileId
                    });
                    db.Teams.Add(new Team
                    {
                        Id = teamId, CompetitionId = competitionId, Name = "Soft-deleted team", CaptainId = userId,
                        MemberIds = [userId], InvitationToken = Guid.NewGuid().ToString("N"),
                        RegisteredAt = now, RegistrationStatus = TeamRegistrationStatus.Approved,
                        DeletedAt = now, AvatarFileId = fileId, WriteUpFileId = fileId,
                        WriteUpSubmittedByUserId = userId, WriteUpSubmittedAt = now
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await AssertFileExistsAsync(options, storage, fileId, objectKey, cancellationToken);
                await using (var db = new NoCtfDbContext(options))
                    await db.Competitions.IgnoreQueryFilters().Where(x => x.Id == competitionId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.PosterFileId, (Guid?)null), cancellationToken);
                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await AssertFileExistsAsync(options, storage, fileId, objectKey, cancellationToken);
                await using (var db = new NoCtfDbContext(options))
                    await db.Teams.IgnoreQueryFilters().Where(x => x.Id == teamId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.AvatarFileId, (Guid?)null), cancellationToken);
                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await AssertFileExistsAsync(options, storage, fileId, objectKey, cancellationToken);
                await using (var db = new NoCtfDbContext(options))
                    await db.Teams.IgnoreQueryFilters().Where(x => x.Id == teamId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.WriteUpFileId, (Guid?)null)
                            .SetProperty(x => x.WriteUpSubmittedByUserId, (Guid?)null)
                            .SetProperty(x => x.WriteUpSubmittedAt, (DateTimeOffset?)null), cancellationToken);
                Func<Task> failedCleanup = () =>
                    HandleCleanupAsync(options, failOnce, fileId, cancellationToken);
                await Assert.That(failedCleanup).Throws<IOException>();
                await AssertFileExistsAsync(
                    options,
                    storage,
                    fileId,
                    objectKey,
                    cancellationToken);

                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
                await using (var verify = new NoCtfDbContext(options))
                {
                    await Assert.That(await verify.Files.AnyAsync(
                        candidate => candidate.Id == fileId,
                        cancellationToken)).IsFalse();
                }
                await Assert.That(await storage.ObjectExists(objectKey, cancellationToken)).IsFalse();

                await HandleCleanupAsync(options, storage, fileId, cancellationToken);
            }
            finally
            {
                if (Directory.Exists(storageRoot))
                    Directory.Delete(storageRoot, recursive: true);
            }
        });
    }

    private static async Task HandleCleanupAsync(
        DbContextOptions<NoCtfDbContext> options,
        IStore storage,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await BackendMessageOperations.CleanupFileAsync(
            new CleanupFile(fileId),
            db,
            storage,
            cancellationToken);
    }

    private static async Task AssertFileExistsAsync(
        DbContextOptions<NoCtfDbContext> options,
        IStore storage,
        Guid fileId,
        string objectKey,
        CancellationToken cancellationToken)
    {
        await using var verify = new NoCtfDbContext(options);
        await Assert.That(await verify.Files.AnyAsync(
            candidate => candidate.Id == fileId,
            cancellationToken)).IsTrue();
        await Assert.That(await storage.ObjectExists(objectKey, cancellationToken)).IsTrue();
    }

    private sealed class FailOnceDeleteStore(IStore inner) : StoreBase
    {
        private bool failNextDelete = true;

        public override Task DeleteObject(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            if (failNextDelete)
            {
                failNextDelete = false;
                throw new IOException("Simulated object storage deletion failure.");
            }

            return inner.DeleteObject(objectKey, cancellationToken);
        }
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

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
