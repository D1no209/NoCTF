using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Application;

public sealed class FixArchivePreparerTests
{
    [Test]
    public async Task PrepareTarAsync_SafeZip_WritesUnderFixedContainerDirectory()
    {
        var root = NewRoot();
        try
        {
            await using var zip = Zip(("fix.sh", "echo ok"), ("files/app.txt", "patched"));
            var output = Path.Combine(root, "payload.tar");
            await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                zip, "fix.zip", Path.Combine(root, "work"), output, CancellationToken.None);

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
            await using var zip = Zip(("../escape.sh", "bad"));
            var action = async () => await new FixArchivePreparer(Configuration()).PrepareTarAsync(
                zip, "fix.zip", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
            await Assert.That(File.Exists(Path.Combine(root, "escape.sh"))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MemoryStream Zip(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(file.Name).Open());
                writer.Write(file.Content);
            }
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
