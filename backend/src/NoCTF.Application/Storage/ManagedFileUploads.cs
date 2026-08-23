using System.Security.Cryptography;
using FluentStorage.Storage;

namespace NoCTF.Application.Storage;

public sealed record ManagedFileUpload(
    Guid FileId,
    string ObjectKey,
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256);

public interface IManagedFileUploadRegistry
{
    Task RegisterAsync(
        ManagedFileUpload file,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task AbandonAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed class ManagedFileUploads(
    IManagedFileUploadRegistry registry,
    IStore objects)
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
            var upload = new ManagedFileUpload(
                fileId,
                objectKey,
                fileName,
                contentType,
                temporary.Length,
                sha256);
            await registry.RegisterAsync(
                upload,
                createdAt,
                cancellationToken);

            try
            {
                temporary.Position = 0;
                await objects.SetObject(
                    objectKey,
                    temporary,
                    contentType,
                    cancellationToken: cancellationToken);
                return upload;
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
}
