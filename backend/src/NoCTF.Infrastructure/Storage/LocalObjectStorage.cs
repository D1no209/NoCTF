using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;

namespace NoCTF.Infrastructure.Storage;

public sealed class LocalObjectStorage(IConfiguration configuration) : IObjectStorage
{
    private readonly string root = Path.GetFullPath(configuration["Storage:LocalRoot"] ?? "storage");
    private readonly Uri baseUri = new(configuration["Storage:PublicBaseUrl"] ?? "http://localhost:5000/");

    public Task<FixUploadGrant> CreateUploadAsync(Guid uploadId, string objectKey, string contentType, long length, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(root, Path.GetDirectoryName(objectKey) ?? string.Empty));
        return Task.FromResult(new FixUploadGrant(uploadId, objectKey,
            new Uri(baseUri, $"storage/uploads/{uploadId}"), DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public async Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var output = File.Create(path))
            await content.CopyToAsync(output, cancellationToken);
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        return new(objectKey, fileName, contentType, new FileInfo(path).Length, hash);
    }

    public async Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        if (!File.Exists(path)) return null;
        await using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        return new(objectKey, Path.GetFileName(path), "application/octet-stream", new FileInfo(path).Length, hash);
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(Resolve(objectKey)));

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(objectKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string objectKey)
    {
        var path = Path.GetFullPath(Path.Combine(root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object key escaped the storage root.");
        return path;
    }
}
