using System.Security.Cryptography;

namespace NoCTF.Application.Storage;

public sealed record ManagedFileUpload(Guid FileId, StoredObject StoredObject);

public interface IManagedFileUploadRegistry
{
    Task RegisterAsync(
        Guid fileId,
        StoredObject metadata,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task AbandonAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed class ManagedFileUploads(
    IManagedFileUploadRegistry registry,
    IObjectStorage objects)
{
    public async Task<ManagedFileUpload> CreateAsync(
        Guid fileId,
        string objectKey,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"noctf-managed-upload-{Guid.NewGuid():N}");
        try
        {
            await using var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan
                    | FileOptions.DeleteOnClose);
            await content.CopyToAsync(temporary, cancellationToken);
            temporary.Position = 0;
            var sha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(temporary, cancellationToken));
            var metadata = new StoredObject(
                objectKey,
                fileName,
                contentType,
                temporary.Length,
                sha256);
            await registry.RegisterAsync(
                fileId,
                metadata,
                createdAt,
                cancellationToken);

            try
            {
                temporary.Position = 0;
                var stored = await objects.PutAsync(
                    objectKey,
                    fileName,
                    contentType,
                    temporary,
                    cancellationToken);
                if (!Matches(metadata, stored))
                {
                    throw new InvalidDataException(
                        "Object storage metadata did not match the prepared upload.");
                }

                return new(fileId, stored);
            }
            catch
            {
                await TryAbandonAsync(fileId);
                throw;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task AbandonAsync(Guid fileId) => TryAbandonAsync(fileId);

    private async Task TryAbandonAsync(Guid fileId)
    {
        try
        {
            await registry.AbandonAsync(fileId, CancellationToken.None);
        }
        catch
        {
            // Registration includes a durable delayed cleanup lease as a backstop.
        }
    }

    private static bool Matches(StoredObject expected, StoredObject actual) =>
        string.Equals(expected.ObjectKey, actual.ObjectKey, StringComparison.Ordinal)
        && string.Equals(expected.FileName, actual.FileName, StringComparison.Ordinal)
        && string.Equals(expected.ContentType, actual.ContentType, StringComparison.OrdinalIgnoreCase)
        && expected.Length == actual.Length
        && string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase);
}
