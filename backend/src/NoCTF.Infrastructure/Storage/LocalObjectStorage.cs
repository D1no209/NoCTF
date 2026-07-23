using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

public sealed class LocalObjectStorage(IConfiguration configuration) : IObjectStorage
{
    private readonly string root = Path.GetFullPath(configuration["Storage:LocalRoot"] ?? "storage");

    public async Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var output = File.Create(path))
            await content.CopyToAsync(output, cancellationToken);
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        await File.WriteAllTextAsync(MetadataPath(path),
            JsonSerializer.Serialize(new LocalObjectMetadata(fileName, contentType, hash)), cancellationToken);
        return new(objectKey, fileName, contentType, new FileInfo(path).Length, hash);
    }

    public async Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        if (!File.Exists(path)) return null;
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        var metadata = await ReadMetadataAsync(path, cancellationToken);
        return new(objectKey, metadata?.FileName ?? Path.GetFileName(path),
            metadata?.ContentType ?? "application/octet-stream", new FileInfo(path).Length,
            metadata?.Sha256 ?? hash);
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(Resolve(objectKey)));

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        if (File.Exists(path)) File.Delete(path);
        var metadataPath = MetadataPath(path);
        if (File.Exists(metadataPath)) File.Delete(metadataPath);
        return Task.CompletedTask;
    }

    private static string MetadataPath(string path) => $"{path}.metadata";

    private static async Task<LocalObjectMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        var metadataPath = MetadataPath(path);
        if (!File.Exists(metadataPath)) return null;
        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<LocalObjectMetadata>(stream, cancellationToken: cancellationToken);
    }

    private sealed record LocalObjectMetadata(string FileName, string ContentType, string Sha256);

    private string Resolve(string objectKey)
    {
        var path = Path.GetFullPath(Path.Combine(root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object key escaped the storage root.");
        return path;
    }
}
