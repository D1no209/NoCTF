using AsyncKeyedLock;
using System.Security.Cryptography;

namespace NoCTF.Runtime.Libvirt;

public sealed class OvaArtifactCache(
    HttpClient httpClient,
    LibvirtRuntimeOptions options)
{
    private readonly AsyncKeyedLocker<string> digestLocks = new(
        o => o.PoolSize = 20,
        StringComparer.Ordinal);

    public async Task<string> ResolveAsync(
        Uri source,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!source.IsAbsoluteUri || source.Scheme is not ("file" or "https"))
            throw new InvalidOperationException("OVA source must use file or https.");
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("OVA SHA-256 digest is invalid.");

        var digest = expectedSha256.ToLowerInvariant();
        using (await digestLocks.LockAsync(digest, cancellationToken))
        {
            Directory.CreateDirectory(options.CacheDirectory);
            var cachedPath = Path.Combine(options.CacheDirectory, $"{digest}.ova");
            if (File.Exists(cachedPath))
            {
                if (await HasDigestAsync(cachedPath, digest, cancellationToken))
                    return cachedPath;
                File.Delete(cachedPath);
            }

            var temporaryPath = Path.Combine(
                options.CacheDirectory,
                $".{digest}.{Guid.NewGuid():N}.tmp");
            try
            {
                await CopySourceAsync(source, temporaryPath, cancellationToken);
                if (!await HasDigestAsync(temporaryPath, digest, cancellationToken))
                    throw new InvalidOperationException("OVA content does not match its SHA-256 digest.");
                try
                {
                    File.Move(temporaryPath, cachedPath);
                }
                catch (IOException)
                {
                    if (!File.Exists(cachedPath)
                        || !await HasDigestAsync(cachedPath, digest, cancellationToken))
                        throw;
                }
                return cachedPath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    private async Task CopySourceAsync(
        Uri source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Scheme == Uri.UriSchemeFile)
        {
            await using var input = new FileStream(
                source.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
            return;
        }

        using var response = await httpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(output, cancellationToken);
    }

    private static async Task<bool> HasDigestAsync(
        string path,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexStringLower(digest),
            expectedDigest,
            StringComparison.Ordinal);
    }
}
