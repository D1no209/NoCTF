using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FluentStorage.Storage;
using NoCTF.Application.Storage;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class CreatePatchUploadTests
{
    [Test]
    public async Task Accepted_upload_preserves_the_authoritative_pending_state()
    {
        var store = new RejectingPatchUploadStore(
            saveState: PatchUploadSaveState.Accepted,
            acceptedState: GameplayFactState.Pending);
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry();
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.State).IsEqualTo(GameplayFactState.Pending);
        await Assert.That(registry.RegisteredFileId).IsNotNull();
        await Assert.That(registry.AbandonedFileId).IsNull();
    }

    [Test]
    public async Task Oversized_archive_is_rejected_before_storage()
    {
        var store = new RejectingPatchUploadStore(maximumArchiveBytes: 1);
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry();
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(PatchUploadFailureCode.ArchiveTooLarge);
        await Assert.That(registry.RegisteredFileId).IsNull();
        await Assert.That(objects.ObjectKey).IsNull();
    }

    [Test]
    public async Task Rejected_database_save_deletes_the_new_object()
    {
        var store = new RejectingPatchUploadStore();
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry();
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(PatchUploadFailureCode.PatchUploadConflict);
        await Assert.That(registry.RegisteredFileId).IsNotNull();
        await Assert.That(registry.AbandonedFileId)
            .IsEqualTo(registry.RegisteredFileId);
    }

    [Test]
    public async Task Cleanup_failure_does_not_mask_the_database_rejection()
    {
        var store = new RejectingPatchUploadStore();
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry(throwOnAbandon: true);
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(PatchUploadFailureCode.PatchUploadConflict);
        await Assert.That(registry.AbandonedFileId)
            .IsEqualTo(registry.RegisteredFileId);
    }

    [Test]
    public async Task Consumed_target_returns_the_stable_failure_and_cleans_the_new_file()
    {
        var store = new RejectingPatchUploadStore(
            saveState: PatchUploadSaveState.DefenseTargetConsumed);
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry();
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(PatchUploadFailureCode.DefenseTargetConsumed);
        await Assert.That(registry.AbandonedFileId)
            .IsEqualTo(registry.RegisteredFileId);
    }

    [Test]
    public async Task Successful_defense_returns_the_stable_failure_and_cleans_the_new_file()
    {
        var store = new RejectingPatchUploadStore(
            saveState: PatchUploadSaveState.AchievementAlreadySucceeded);
        var objects = new RecordingStore();
        var registry = new RecordingUploadRegistry();
        var useCase = new CreatePatchUpload(
            store,
            new ManagedFileUploads(registry, objects));
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(PatchUploadFailureCode.DefenseAlreadySucceeded);
        await Assert.That(registry.AbandonedFileId)
            .IsEqualTo(registry.RegisteredFileId);
    }

    private static byte[] CreatePatchArchive()
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
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes("echo fix")))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
            {
                DataStream = data
            });
        }
        return output.ToArray();
    }

    private sealed class RejectingPatchUploadStore(
        long maximumArchiveBytes = PatchUploadRules.DefaultMaximumArchiveBytes,
        PatchUploadSaveState saveState = PatchUploadSaveState.ConcurrencyConflict,
        GameplayFactState acceptedState = GameplayFactState.Processing)
        : IPatchUploadStore
    {
        public Task<PatchUploadScope?> ResolveScopeAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid runtimeInstanceId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PatchUploadScope?>(new(
                competitionId,
                competitionChallengeId,
                Guid.CreateVersion7(),
                userId,
                runtimeInstanceId,
                maximumArchiveBytes));

        public Task<PatchUploadSaveResult> SaveAsync(
            Guid patchUploadId,
            Guid gameplayFactId,
            PatchUploadScope scope,
            Guid fileId,
            DateTimeOffset uploadedAt,
            CancellationToken cancellationToken) => Task.FromResult(
                saveState == PatchUploadSaveState.Accepted
                    ? new PatchUploadSaveResult(
                        saveState,
                        gameplayFactId,
                        acceptedState)
                    : new PatchUploadSaveResult(saveState));
    }

    private sealed class RecordingStore : StoreBase
    {
        public string? ObjectKey { get; private set; }

        public override async Task SetObject(
            string objectKey,
            Stream content,
            string contentType,
            bool append = false,
            CancellationToken cancellationToken = default)
        {
            ObjectKey = objectKey;
            await content.CopyToAsync(Stream.Null, cancellationToken);
        }
    }

    private sealed class RecordingUploadRegistry(bool throwOnAbandon = false)
        : IManagedFileUploadRegistry
    {
        public Guid? RegisteredFileId { get; private set; }
        public Guid? AbandonedFileId { get; private set; }

        public Task RegisterAsync(
            ManagedFileUpload file,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            RegisteredFileId = file.FileId;
            return Task.CompletedTask;
        }

        public Task AbandonAsync(Guid fileId, CancellationToken cancellationToken)
        {
            AbandonedFileId = fileId;
            return throwOnAbandon
                ? Task.FromException(new IOException("cleanup failed"))
                : Task.CompletedTask;
        }
    }
}
