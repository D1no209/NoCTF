using System.Security.Cryptography;
using System.Text;
using NoCTF.PluginBase;

namespace NoCTF.Infrastructure.Storage;

public class LocalFileStorageProvider(string basePath, string? signingKey = null) : ITemporaryUrlStorageProvider
{
    private readonly string _rootPath = Path.GetFullPath(basePath);

    public Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(fileName);
        EnsureNoReparsePoints(path);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(fileName);
        EnsureNoReparsePoints(path);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string> GetUrlAsync(string fileName, CancellationToken cancellationToken = default)
        => GetUrlAsync(fileName, TimeSpan.FromHours(1), cancellationToken);

    public Task<string> GetUrlAsync(
        string fileName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var normalized = StorageObjectKey.Normalize(fileName);
        var url = $"/api/files/{normalized}";
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            var effectiveLifetime = lifetime <= TimeSpan.Zero
                ? TimeSpan.FromMinutes(1)
                : lifetime > TimeSpan.FromDays(7)
                    ? TimeSpan.FromDays(7)
                    : lifetime;
            var expires = DateTimeOffset.UtcNow.Add(effectiveLifetime).ToUnixTimeSeconds();
            var signature = LocalFileUrlSigner.Sign(normalized, expires, signingKey);
            url = $"{url}?expires={expires}&sig={Uri.EscapeDataString(signature)}";
        }

        return Task.FromResult(url);
    }

    public async Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var normalized = StorageObjectKey.Normalize(fileName);
        var path = ResolvePath(fileName);
        var directory = Path.GetDirectoryName(path);
        EnsureNoReparsePoints(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(path);

        var temporaryPath = Path.Combine(
            directory ?? _rootPath,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.upload");
        try
        {
            await using (var fileStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(path);
            File.Move(temporaryPath, path, overwrite: true);
            return normalized;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private string ResolvePath(string fileName)
    {
        var normalized = StorageObjectKey.Normalize(fileName);
        var path = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootWithSeparator = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage key escapes the configured root.");
        return path;
    }

    private void EnsureNoReparsePoints(string path)
    {
        var current = _rootPath;
        RejectReparsePoint(current);
        var relative = Path.GetRelativePath(_rootPath, path);
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Storage path contains a symbolic link or reparse point.");
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
