using Microsoft.Extensions.Configuration;
using NoCTF.Infrastructure;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests;

public class StorageProviderTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("Local", true)]
    [InlineData("S3", false)]
    [InlineData("MinIO", false)]
    public void StorageProviderFactory_DetectsLocalStorage(string? providerType, bool expected)
    {
        var values = new Dictionary<string, string?>();
        if (providerType is not null)
            values["StorageProvider:Type"] = providerType;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Equal(expected, StorageProviderFactory.UsesLocalStorage(configuration));
    }

    [Fact]
    public void StorageProviderFactory_RequiresDedicatedSigningKeyForLocalStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StorageProvider:Type"] = "Local",
                ["JwtSettings:Secret"] = new string('j', 64)
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => StorageProviderFactory.ValidateLocalUrlSigningKey(configuration));

        Assert.Contains("StorageProvider:Local:UrlSigningKey", exception.Message);
    }

    [Fact]
    public void StorageProviderFactory_ValidatesS3ConfigurationWithoutLocalSigningKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StorageProvider:Type"] = "S3",
                ["StorageProvider:S3:Endpoint"] = "https://objects.example.test",
                ["StorageProvider:S3:Bucket"] = "noctf",
                ["StorageProvider:S3:AccessKey"] = "A1B2C3D4E5F6G7H8",
                ["StorageProvider:S3:SecretKey"] = "V7mZ4-rQ2x-H9pL6-kT8w"
            })
            .Build();

        StorageProviderFactory.ValidateLocalUrlSigningKey(configuration);
    }

    [Fact]
    public void StorageProviderFactory_RejectsMissingS3Credentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StorageProvider:Type"] = "S3",
                ["StorageProvider:S3:Endpoint"] = "https://objects.example.test",
                ["StorageProvider:S3:Bucket"] = "noctf"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => StorageProviderFactory.ValidateLocalUrlSigningKey(configuration));

        Assert.Contains("StorageProvider:S3:AccessKey", exception.Message);
    }

    [Fact]
    public async Task LocalFileStorageProvider_Upload_Download_Delete_Works()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));

        await provider.UploadAsync("test.txt", content, "text/plain");

        string text;
        var downloaded = await provider.DownloadAsync("test.txt");
        using (var reader = new StreamReader(downloaded))
        {
            text = await reader.ReadToEndAsync();
        }

        Assert.Equal("hello world", text);

        await provider.DeleteAsync("test.txt");
        Assert.False(File.Exists(Path.Combine(basePath, "test.txt")));

        if (Directory.Exists(basePath))
            Directory.Delete(basePath, true);
    }

    [Fact]
    public async Task LocalFileStorageProvider_GetUrl_ReturnsApiPath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);

        var url = await provider.GetUrlAsync("competition1/file.zip");

        Assert.Equal("/api/files/competition1/file.zip", url);
    }

    [Fact]
    public async Task LocalFileStorageProvider_GetUrl_WithSigningKey_SignsAttachmentUrls()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath, "local-file-signing-secret");

        var url = await provider.GetUrlAsync("challenge-attachments/competition1/file.zip");

        Assert.StartsWith("/api/files/challenge-attachments/competition1/file.zip?", url);
        var query = ParseQuery(url);
        Assert.True(long.TryParse(query["expires"], out var expires));
        Assert.True(LocalFileUrlSigner.Validate(
            "challenge-attachments/competition1/file.zip",
            expires,
            query["sig"],
            "local-file-signing-secret",
            DateTimeOffset.UtcNow));
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        Assert.True(queryStart >= 0);
        return url[(queryStart + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.Ordinal);
    }

    [Fact]
    public async Task LocalFileStorageProvider_Upload_CreatesSubdirectories()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));

        await provider.UploadAsync("subdir/nested/file.txt", content, "text/plain");

        Assert.True(File.Exists(Path.Combine(basePath, "subdir", "nested", "file.txt")));

        Directory.Delete(basePath, true);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("nested/../../outside.txt")]
    [InlineData("%2e%2e/outside.txt")]
    [InlineData("nested/%2E%2E/outside.txt")]
    [InlineData("%252e%252e/outside.txt")]
    [InlineData("nested//file.txt")]
    [InlineData("nested/file.txt/")]
    [InlineData("nested/%ZZ/file.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData("C:\\absolute\\path.txt")]
    [InlineData("nested/file.txt:secret")]
    [InlineData("nested/CON.txt")]
    [InlineData("nested/LPT1")]
    [InlineData("nested/file|name.txt")]
    public async Task LocalFileStorageProvider_Rejects_PathTraversal(string key)
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UploadAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DownloadAsync(key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DeleteAsync(key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetUrlAsync(key));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("%252e%252e/outside.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData("C:\\absolute\\path.txt")]
    [InlineData("nested//file.txt")]
    [InlineData("nested/file.txt:secret")]
    [InlineData("nested/CON.txt")]
    public async Task S3StorageProvider_Rejects_InvalidObjectKeys(string key)
    {
        using var provider = new S3StorageProvider(
            "https://objects.example.test",
            "noctf",
            "access-key",
            "secret-key");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetUrlAsync(key));
    }

    [Fact]
    public async Task LocalFileStorageProvider_CancelledUpload_DoesNotLeavePartialObject()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);
        await provider.UploadAsync("object.bin", new MemoryStream([9, 9, 9]), "application/octet-stream");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.UploadAsync(
            "object.bin",
            new MemoryStream(new byte[1024]),
            "application/octet-stream",
            cancellation.Token));

        await using (var existing = await provider.DownloadAsync("object.bin"))
        {
            using var buffer = new MemoryStream();
            await existing.CopyToAsync(buffer);
            Assert.Equal([9, 9, 9], buffer.ToArray());
        }
        Assert.Empty(Directory.EnumerateFiles(basePath, "*.upload", SearchOption.AllDirectories));

        Directory.Delete(basePath, recursive: true);
    }
}
