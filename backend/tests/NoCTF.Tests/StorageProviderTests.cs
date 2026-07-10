using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests;

public class StorageProviderTests
{
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
    [InlineData("/absolute/path.txt")]
    [InlineData("C:\\absolute\\path.txt")]
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
}
