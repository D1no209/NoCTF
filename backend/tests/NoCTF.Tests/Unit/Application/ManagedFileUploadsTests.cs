using System.Security.Cryptography;
using FluentStorage.Storage;
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

        await Assert.That(string.Join(",", calls)).IsEqualTo("register,set");
        await Assert.That(uploaded.FileId).IsEqualTo(fileId);
        await Assert.That(registry.Metadata).IsNotNull();
        await Assert.That(registry.Metadata!.ByteLength).IsEqualTo(contentBytes.Length);
        await Assert.That(registry.Metadata.Sha256)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(contentBytes)));
        await Assert.That(registry.CreatedAt).IsEqualTo(now);
    }

    [Test]
    public async Task Storage_failure_abandons_the_registered_file()
    {
        var calls = new List<string>();
        var registry = new RecordingRegistry(calls);
        var storage = new RecordingStorage(calls, throwOnSet: true);
        var uploads = new ManagedFileUploads(registry, storage);
        var fileId = Guid.CreateVersion7();
        await using var content = new MemoryStream("failure"u8.ToArray());

        Func<Task> action = () => uploads.CreateAsync(
            fileId,
            "attachments/failure",
            "failure.txt",
            "text/plain",
            content,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        await Assert.That(action).Throws<IOException>();
        await Assert.That(string.Join(",", calls)).IsEqualTo("register,set,abandon");
        await Assert.That(registry.AbandonedFileId).IsEqualTo(fileId);
    }

    [Test]
    public async Task Cleanup_queue_failure_does_not_mask_the_storage_failure()
    {
        var calls = new List<string>();
        var registry = new RecordingRegistry(calls, throwOnAbandon: true);
        var storage = new RecordingStorage(calls, throwOnSet: true);
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
        await Assert.That(string.Join(",", calls)).IsEqualTo("register,set,abandon");
    }

    private sealed class RecordingRegistry(
        List<string> calls,
        bool throwOnAbandon = false) : IManagedFileUploadRegistry
    {
        public ManagedFileUpload? Metadata { get; private set; }
        public DateTimeOffset? CreatedAt { get; private set; }
        public Guid? AbandonedFileId { get; private set; }

        public Task RegisterAsync(
            ManagedFileUpload file,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            calls.Add("register");
            Metadata = file;
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
        bool throwOnSet = false) : StoreBase
    {
        public override async Task SetObject(
            string objectKey,
            Stream content,
            string contentType,
            bool append = false,
            CancellationToken cancellationToken = default)
        {
            calls.Add("set");
            if (throwOnSet)
                throw new IOException("storage failed");
            await content.CopyToAsync(Stream.Null, cancellationToken);
        }
    }
}
