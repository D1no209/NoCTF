using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Common;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Submissions.Intake;
using NoCTF.Infrastructure.Submissions.PatchUploads;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("PatchUploadReplacement")]
[NotInParallel]
public sealed class PatchUploadReplacementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task New_upload_replaces_only_the_unconsumed_draft_and_durably_cleans_its_object(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_patch_replacement_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-patch-replacement-{Guid.NewGuid():N}");
            Directory.CreateDirectory(storageRoot);
            using var host = BuildHost(
                postgres.GetConnectionString(),
                storageRoot);
            try
            {
                var fixture = await SeedAsync(host, cancellationToken);
                await host.StartAsync(cancellationToken);

                var objects = host.Services.GetRequiredService<IObjectStorage>();
                var originalArchive = CreatePatchArchive("echo original");
                await using (var content = new MemoryStream(originalArchive))
                {
                    var stored = await objects.PutAsync(
                        fixture.OriginalObjectKey,
                        "original.tar.gz",
                        "application/gzip",
                        content,
                        cancellationToken);
                    _ = await AddOriginalUploadAsync(
                        host,
                        fixture,
                        stored,
                        cancellationToken);
                }

                var replacement = await UploadAsync(
                    host,
                    fixture,
                    "echo replacement",
                    fixture.Now.AddSeconds(1),
                    cancellationToken);
                await Assert.That(replacement.Succeeded).IsTrue();

                var replacementId = replacement.Value!.PatchUploadId;
                string replacementObjectKey;
                await using (var scope = host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var uploads = await db.PatchUploads.AsNoTracking()
                        .Include(upload => upload.File)
                        .Where(upload =>
                            upload.TeamId == fixture.TeamId
                            && upload.CompetitionChallengeId == fixture.CompetitionChallengeId)
                        .ToArrayAsync(cancellationToken);
                    await Assert.That(uploads.Length).IsEqualTo(1);
                    await Assert.That(uploads[0].Id).IsEqualTo(replacementId);
                    await Assert.That(uploads[0].ConsumedAt).IsNull();
                    replacementObjectKey = uploads[0].ObjectKey;
                }

                await WaitUntilDeletedAsync(
                    objects,
                    fixture.OriginalObjectKey,
                    cancellationToken);
                await Assert.That(await objects.InspectAsync(
                    replacementObjectKey,
                    cancellationToken)).IsNotNull();

                await ConsumeAsync(
                    host,
                    fixture,
                    replacementId,
                    fixture.Now.AddSeconds(2),
                    cancellationToken);
                var next = await UploadAsync(
                    host,
                    fixture,
                    "echo next",
                    fixture.Now.AddSeconds(3),
                    cancellationToken);
                await Assert.That(next.Succeeded).IsTrue();

                await using (var scope = host.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var uploads = await db.PatchUploads.AsNoTracking()
                        .Where(upload =>
                            upload.TeamId == fixture.TeamId
                            && upload.CompetitionChallengeId == fixture.CompetitionChallengeId)
                        .OrderBy(upload => upload.UploadedAt)
                        .ToArrayAsync(cancellationToken);
                    await Assert.That(uploads.Length).IsEqualTo(2);
                    await Assert.That(uploads[0].Id).IsEqualTo(replacementId);
                    await Assert.That(uploads[0].ConsumedAt).IsNotNull();
                    await Assert.That(uploads[0].SubmissionId).IsNotNull();
                    await Assert.That(uploads[1].Id).IsEqualTo(next.Value!.PatchUploadId);
                    await Assert.That(uploads[1].ConsumedAt).IsNull();
                }
                await Assert.That(await objects.InspectAsync(
                    replacementObjectKey,
                    cancellationToken)).IsNotNull();
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
                if (Directory.Exists(storageRoot))
                    Directory.Delete(storageRoot, recursive: true);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Replacement_and_fix_consumption_preserve_the_attempt_invariant(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_patch_concurrency_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            var storageRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-patch-concurrency-{Guid.NewGuid():N}");
            Directory.CreateDirectory(storageRoot);
            using var host = BuildHost(
                postgres.GetConnectionString(),
                storageRoot);
            try
            {
                var fixture = await SeedAsync(host, cancellationToken);
                await host.StartAsync(cancellationToken);
                var objects = host.Services.GetRequiredService<IObjectStorage>();

                var originalArchive = CreatePatchArchive("echo original");
                Guid originalUploadId;
                await using (var content = new MemoryStream(originalArchive))
                {
                    var stored = await objects.PutAsync(
                        fixture.OriginalObjectKey,
                        "original.tar.gz",
                        "application/gzip",
                        content,
                        cancellationToken);
                    originalUploadId = await AddOriginalUploadAsync(
                        host,
                        fixture,
                        stored,
                        cancellationToken);
                }

                var replacementUploadId = Guid.CreateVersion7(fixture.Now.AddSeconds(1));
                var replacementObjectKey = $"fix-uploads/{replacementUploadId:N}";
                StoredObject replacementObject;
                await using (var content = new MemoryStream(
                                 CreatePatchArchive("echo replacement")))
                {
                    replacementObject = await objects.PutAsync(
                        replacementObjectKey,
                        "replacement.tar.gz",
                        "application/gzip",
                        content,
                        cancellationToken);
                }

                var replacementTask = SaveReplacementAsync(
                    host,
                    fixture,
                    replacementUploadId,
                    replacementObject,
                    fixture.Now.AddSeconds(1),
                    cancellationToken);
                var submissionTask = SubmitAsync(
                    host,
                    fixture,
                    originalUploadId,
                    fixture.Now.AddSeconds(1),
                    cancellationToken);

                var replacementSaved = await replacementTask.WaitAsync(
                    TimeSpan.FromSeconds(2), cancellationToken);
                var submission = await submissionTask.WaitAsync(
                    TimeSpan.FromSeconds(2), cancellationToken);
                await Assert.That(replacementSaved).IsTrue();
                await Assert.That(
                    submission.Succeeded
                    || submission.FailureCode == SubmissionFailureCode.PatchUploadNotFound).IsTrue();

                await using (var verifyScope = host.Services.CreateAsyncScope())
                {
                    var db = verifyScope.ServiceProvider
                        .GetRequiredService<NoCtfDbContext>();
                    var replacement = await db.PatchUploads.AsNoTracking()
                        .SingleAsync(
                            upload => upload.Id == replacementUploadId,
                            cancellationToken);
                    await Assert.That(replacement.ConsumedAt).IsNull();
                    await Assert.That(replacement.SubmissionId).IsNull();

                    var original = await db.PatchUploads.AsNoTracking()
                        .SingleOrDefaultAsync(
                            upload => upload.Id == originalUploadId,
                            cancellationToken);
                    if (submission.Succeeded)
                    {
                        await Assert.That(original).IsNotNull();
                        var consumedOriginal = original!;
                        await Assert.That(consumedOriginal.ConsumedAt).IsNotNull();
                        await Assert.That(consumedOriginal.SubmissionId)
                            .IsEqualTo(submission.Value!.SubmissionId);
                    }
                    else
                    {
                        await Assert.That(original).IsNull();
                    }
                }

                if (submission.Succeeded)
                {
                    await Assert.That(await objects.InspectAsync(
                        fixture.OriginalObjectKey,
                        cancellationToken)).IsNotNull();
                }
                else
                {
                    await WaitUntilDeletedAsync(
                        objects,
                        fixture.OriginalObjectKey,
                        cancellationToken);
                }
                await Assert.That(await objects.InspectAsync(
                    replacementObjectKey,
                    cancellationToken)).IsNotNull();
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
                if (Directory.Exists(storageRoot))
                    Directory.Delete(storageRoot, recursive: true);
            }
        });
    }

    private static IHost BuildHost(
        string connectionString,
        string storageRoot)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:LocalRoot"] = storageRoot;
        builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<ITransactionalMessageOutbox, WolverineTransactionalMessageOutbox>();
        builder.Services.AddScoped<IPatchUploadStore, PatchUploadStore>();
        builder.Services.AddScoped<CreatePatchUpload>();
        builder.Services.AddSingleton<IObjectStorage, LocalObjectStorage>();
        builder.UseWolverine(options =>
        {
            options.Discovery.IncludeType<PatchUploadCleanupHandler>();
            options.PersistMessagesWithPostgresql(
                connectionString,
                "wolverine_patch_replacement_test");
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.ListenToPostgresqlQueue("patch-upload-cleanup-test")
                .UseDurableInbox();
            options.PublishMessage<CleanupFile>()
                .ToPostgresqlQueue("patch-upload-cleanup-test");
        });
        return builder.Build();
    }

    private static async Task<Fixture> SeedAsync(
        IHost host,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        db.Users.AddRange(
            NewUser(ownerId, "owner", now),
            NewUser(memberId, "member", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Patch replacement",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Status = CompetitionStatus.Running,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = "service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson =
                """{"schemaVersion":1,"break":null,"fix":null,"requireBreakBeforeFix":false,"maxBreakSubmissions":null,"maxFixSubmissions":null}""",
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionId,
            competitionChallengeId,
            teamId,
            memberId,
            $"fix-uploads/{Guid.CreateVersion7(now):N}");
    }

    private static async Task<Guid> AddOriginalUploadAsync(
        IHost host,
        Fixture fixture,
        StoredObject stored,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var patchUploadId = Guid.CreateVersion7(fixture.Now);
        var file = new StoredFile
        {
            Id = Guid.CreateVersion7(fixture.Now),
            ObjectKey = stored.ObjectKey,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            ByteLength = stored.Length,
            Sha256 = Convert.FromHexString(stored.Sha256),
            CreatedAt = fixture.Now
        };
        db.Files.Add(file);
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamId,
            UploadedByUserId = fixture.MemberId,
            FileId = file.Id,
            File = file,
            UploadedAt = fixture.Now
        });
        await db.SaveChangesAsync(cancellationToken);
        return patchUploadId;
    }

    private static async Task<OperationResult<CreatedPatchUpload, PatchUploadFailureCode>> UploadAsync(
        IHost host,
        Fixture fixture,
        string script,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await using var content = new MemoryStream(CreatePatchArchive(script));
        return await scope.ServiceProvider.GetRequiredService<CreatePatchUpload>()
            .ExecuteAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.MemberId,
                "fix.tar.gz",
                "application/gzip",
                content,
                uploadedAt,
                cancellationToken);
    }

    private static async Task ConsumeAsync(
        IHost host,
        Fixture fixture,
        Guid patchUploadId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var result = await new SubmitFix(
                new SubmissionIntakeStore(
                    db,
                    new OpenApiTransactionalMessageOutbox()),
                new GameModeSubmissionAdmissionPolicy())
            .ExecuteAsync(
                new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.MemberId,
                    patchUploadId,
                    receivedAt),
                cancellationToken);
        await Assert.That(result.Succeeded).IsTrue();
    }

    private static async Task<OperationResult<SubmissionAccepted, SubmissionFailureCode>> SubmitAsync(
        IHost host,
        Fixture fixture,
        Guid patchUploadId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        return await new SubmitFix(
                new SubmissionIntakeStore(
                    db,
                    new OpenApiTransactionalMessageOutbox()),
                new GameModeSubmissionAdmissionPolicy())
            .ExecuteAsync(
                new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.MemberId,
                    patchUploadId,
                    receivedAt),
                cancellationToken);
    }

    private static async Task<bool> SaveReplacementAsync(
        IHost host,
        Fixture fixture,
        Guid patchUploadId,
        StoredObject stored,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var fileId = Guid.CreateVersion7(uploadedAt);
        db.Files.Add(new StoredFile
        {
            Id = fileId,
            ObjectKey = stored.ObjectKey,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            ByteLength = stored.Length,
            Sha256 = Convert.FromHexString(stored.Sha256),
            CreatedAt = uploadedAt
        });
        await db.SaveChangesAsync(cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<IPatchUploadStore>()
            .SaveAsync(
                patchUploadId,
                new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.TeamId,
                    fixture.MemberId),
                fileId,
                uploadedAt,
                cancellationToken);
    }

    private static async Task WaitUntilDeletedAsync(
        IObjectStorage objects,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (await objects.InspectAsync(objectKey, cancellationToken) is not null)
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("The durable cleanup message did not delete the replaced object.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private static byte[] CreatePatchArchive(string script)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(
                   output,
                   CompressionLevel.SmallestSize,
                   leaveOpen: true))
        using (var writer = new TarWriter(
                   gzip,
                   TarEntryFormat.Pax,
                   leaveOpen: true))
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes(script)))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
            {
                DataStream = data
            });
        }
        return output.ToArray();
    }

    private static User NewUser(
        Guid id,
        string name,
        DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid MemberId,
        string OriginalObjectKey);
}

public sealed class PatchUploadCleanupHandler
{
    public static Task Handle(
        CleanupFile message,
        NoCtfDbContext db,
        IObjectStorage objects,
        CancellationToken cancellationToken) =>
        BackendMessageHandlers.Handle(message, db, objects, cancellationToken);
}
