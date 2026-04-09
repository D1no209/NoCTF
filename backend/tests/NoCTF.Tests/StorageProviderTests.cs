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
    public async Task LocalFileStorageProvider_Upload_CreatesSubdirectories()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var provider = new LocalFileStorageProvider(basePath);
        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data"));

        await provider.UploadAsync("subdir/nested/file.txt", content, "text/plain");

        Assert.True(File.Exists(Path.Combine(basePath, "subdir", "nested", "file.txt")));

        Directory.Delete(basePath, true);
    }
}
