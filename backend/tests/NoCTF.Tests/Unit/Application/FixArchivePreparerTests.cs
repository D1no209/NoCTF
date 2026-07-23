using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Application;

public sealed class FixArchivePreparerTests
{
    [Test]
    public async Task PrepareTarAsync_SafeTarGzip_WritesUnderFixedContainerDirectory()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip(("fix.sh", "echo ok"), ("files/app.txt", "patched"));
            var output = Path.Combine(root, "payload.tar");
            await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                archive, "fix.tar.gz", Path.Combine(root, "work"), output, CancellationToken.None);

            await using var stream = File.OpenRead(output);
            using var reader = new TarReader(stream);
            var names = new List<string>();
            while (await reader.GetNextEntryAsync() is { } entry) names.Add(entry.Name.Replace('\\', '/'));

            await Assert.That(names).Contains("noctf/fix/fix.sh");
            await Assert.That(names).Contains("noctf/fix/files/app.txt");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_PathTraversal_RejectsBeforeWritingOutsideSandbox()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip(("../escape.sh", "bad"));
            var action = async () => await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                archive, "fix.tar.gz", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
            await Assert.That(File.Exists(Path.Combine(root, "escape.sh"))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_RejectsZipEvenWithZipExtension()
    {
        var root = NewRoot();
        try
        {
            await using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("fix.sh").Open()))
                writer.Write("echo ok");
            zip.Position = 0;
            var action = async () => await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                zip, "fix.zip", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_RejectsDuplicateCaseInsensitivePaths()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip(("fix.sh", "one"), ("FIX.SH", "two"));
            var action = async () => await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                archive, "fix.tar.gz", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MemoryStream TarGzip(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
            foreach (var file in files)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Name)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(file.Content))
                });
        stream.Position = 0;
        return stream;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"noctf-archive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
