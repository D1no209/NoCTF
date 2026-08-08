using System.Security.Cryptography;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class ManagedFileUploadsTests
{
    [Test]
    public async Task File_metadata_is_registered_before_the_final_object_is_uploaded()
    {
        var calls = new List<string>();
        var registry = new RecordingRegistry(calls);
        var storage = new RecordingStorage(calls);
        var uploads = new ManagedFileUploads(registry, storage);
        var fileId = Guid.CreateVersion7();
        var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        var contentBytes = "managed upload"u8.ToArray();
        await using var content = new MemoryStream(contentBytes);

        var uploaded = await uploads.CreateAsync(
            fileId,
            "attachments/example",
            "example.txt",
            "text/plain",
            content,
            now,
            CancellationToken.None);

        await Assert.That(string.Join(",", calls)).IsEqualTo("register,put");
        await Assert.That(uploaded.FileId).IsEqualTo(fileId);
        await Assert.That(registry.Metadata).IsNotNull();
        await Assert.That(registry.Metadata!.Length).IsEqualTo(contentBytes.Length);
        await Assert.That(registry.Metadata.Sha256)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(contentBytes)));
        await Assert.That(registry.CreatedAt).IsEqualTo(now);
    }

    [Test]
    public async Task Mismatched_storage_metadata_abandons_the_registered_file()
    {
        var calls = new List<string>();
        var registry = new RecordingRegistry(calls);
        var storage = new RecordingStorage(calls, corruptMetadata: true);
        var uploads = new ManagedFileUploads(registry, storage);
        var fileId = Guid.CreateVersion7();
        await using var content = new MemoryStream("mismatch"u8.ToArray());

        Func<Task> action = () => uploads.CreateAsync(
            fileId,
            "attachments/mismatch",
            "mismatch.txt",
            "text/plain",
            content,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        await Assert.That(action).Throws<InvalidDataException>();
        await Assert.That(string.Join(",", calls)).IsEqualTo("register,put,abandon");
        await Assert.That(registry.AbandonedFileId).IsEqualTo(fileId);
    }

    [Test]
    public async Task Cleanup_queue_failure_does_not_mask_the_storage_failure()
    {
        var calls = new List<string>();
        var registry = new RecordingRegistry(calls, throwOnAbandon: true);
        var storage = new RecordingStorage(calls, throwOnPut: true);
        var uploads = new ManagedFileUploads(registry, storage);
        await using var content = new MemoryStream("failure"u8.ToArray());

        Func<Task> action = () => uploads.CreateAsync(
            Guid.CreateVersion7(),
            "attachments/failure",
            "failure.txt",
            "text/plain",
            content,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var exception = await Assert.That(action).Throws<IOException>();
        await Assert.That(exception!.Message).IsEqualTo("storage failed");
        await Assert.That(string.Join(",", calls)).IsEqualTo("register,put,abandon");
    }

    private sealed class RecordingRegistry(
        List<string> calls,
        bool throwOnAbandon = false) : IManagedFileUploadRegistry
    {
        public StoredObject? Metadata { get; private set; }
        public DateTimeOffset? CreatedAt { get; private set; }
        public Guid? AbandonedFileId { get; private set; }

        public Task RegisterAsync(
            Guid fileId,
            StoredObject metadata,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            calls.Add("register");
            Metadata = metadata;
            CreatedAt = createdAt;
            return Task.CompletedTask;
        }

        public Task AbandonAsync(Guid fileId, CancellationToken cancellationToken)
        {
            calls.Add("abandon");
            AbandonedFileId = fileId;
            return throwOnAbandon
                ? Task.FromException(new InvalidOperationException("cleanup queue failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingStorage(
        List<string> calls,
        bool corruptMetadata = false,
        bool throwOnPut = false) : IObjectStorage
    {
        public Task<StoredObject?> InspectAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<StoredObject> PutAsync(
            string objectKey,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken)
        {
            calls.Add("put");
            if (throwOnPut)
                throw new IOException("storage failed");
            var start = content.Position;
            var sha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(content, cancellationToken));
            return new(
                objectKey,
                fileName,
                contentType,
                content.Position - start,
                corruptMetadata ? new string('0', 64) : sha256);
        }

        public Task<Stream> OpenReadAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
