using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.PatchUploads;

namespace NoCTF.Tests.Unit.Application;

public sealed class CreatePatchUploadTests
{
    [Test]
    public async Task Rejected_database_save_deletes_the_new_object()
    {
        var store = new RejectingPatchUploadStore();
        var objects = new RecordingObjectStorage();
        var useCase = new CreatePatchUpload(store, objects);
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("patch_upload_conflict");
        await Assert.That(objects.StoredObjectKey).IsNotNull();
        await Assert.That(objects.DeletedObjectKey)
            .IsEqualTo(objects.StoredObjectKey);
    }

    [Test]
    public async Task Cleanup_failure_does_not_mask_the_database_rejection()
    {
        var store = new RejectingPatchUploadStore();
        var objects = new RecordingObjectStorage(throwOnDelete: true);
        var useCase = new CreatePatchUpload(store, objects);
        await using var archive = new MemoryStream(CreatePatchArchive());

        var result = await useCase.ExecuteAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fix.tar.gz",
            "application/gzip",
            archive,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("patch_upload_conflict");
        await Assert.That(objects.DeletedObjectKey)
            .IsEqualTo(objects.StoredObjectKey);
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

    private sealed class RejectingPatchUploadStore : IPatchUploadStore
    {
        public Task<PatchUploadScope?> ResolveScopeAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PatchUploadScope?>(new(
                competitionId,
                competitionChallengeId,
                Guid.CreateVersion7(),
                userId));

        public Task<bool> SaveAsync(
            Guid patchUploadId,
            PatchUploadScope scope,
            string objectKey,
            string fileName,
            string contentType,
            long byteLength,
            byte[] sha256,
            DateTimeOffset uploadedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class RecordingObjectStorage(bool throwOnDelete = false)
        : IObjectStorage
    {
        public string? StoredObjectKey { get; private set; }
        public string? DeletedObjectKey { get; private set; }

        public Task<StoredObject?> InspectAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<StoredObject?>(null);

        public Task<StoredObject> PutAsync(
            string objectKey,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken)
        {
            StoredObjectKey = objectKey;
            return Task.FromResult(new StoredObject(
                objectKey,
                fileName,
                contentType,
                content.Length,
                new string('0', 64)));
        }

        public Task<Stream> OpenReadAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string objectKey,
            CancellationToken cancellationToken)
        {
            DeletedObjectKey = objectKey;
            return throwOnDelete
                ? Task.FromException(new IOException("cleanup failed"))
                : Task.CompletedTask;
        }
    }
}
