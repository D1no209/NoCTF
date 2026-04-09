using NoCTF.PluginBase;

namespace NoCTF.Infrastructure.Storage;

public class LocalFileStorageProvider(string basePath) : IStorageProvider
{
    public Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(basePath, fileName);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(basePath, fileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"/api/files/{fileName.TrimStart('/').Replace("\\", "/")}");
    }

    public async Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(basePath, fileName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return fileName;
    }
}
