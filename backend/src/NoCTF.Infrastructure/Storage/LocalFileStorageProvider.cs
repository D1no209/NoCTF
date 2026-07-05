using System.Security.Cryptography;
using System.Text;
using NoCTF.PluginBase;

namespace NoCTF.Infrastructure.Storage;

public class LocalFileStorageProvider(string basePath, string? signingKey = null) : IStorageProvider
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
        var normalized = fileName.TrimStart('/').Replace("\\", "/");
        var url = $"/api/files/{normalized}";
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var signature = LocalFileUrlSigner.Sign(normalized, expires, signingKey);
            url = $"{url}?expires={expires}&sig={Uri.EscapeDataString(signature)}";
        }

        return Task.FromResult(url);
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

public static class LocalFileUrlSigner
{
    public static string Sign(string path, long expires, string key)
    {
        var payload = $"{Normalize(path)}\n{expires}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool Validate(string path, long expires, string? signature, string? key, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(signature))
            return false;
        if (expires < now.ToUnixTimeSeconds())
            return false;

        var expected = Sign(path, expires, key);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signature);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string Normalize(string path)
        => path.TrimStart('/').Replace("\\", "/", StringComparison.Ordinal);
}
