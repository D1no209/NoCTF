using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Unit.Application;

public sealed class FixArchivePreparerTests
{
    [Test]
    public async Task InvalidExtractionLimit_FailsDuringHostStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{FixVerificationOptions.SectionName}:MaxExpandedBytes"] = "0"
        });
        builder.Services.AddSingleton<IValidateOptions<FixVerificationOptions>,
            FixVerificationOptionsValidator>();
        builder.Services.AddOptions<FixVerificationOptions>()
            .Bind(builder.Configuration.GetSection(FixVerificationOptions.SectionName))
            .ValidateOnStart();
        using var host = builder.Build();

        var action = () => host.StartAsync(CancellationToken.None);

        await Assert.That(action).Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments(TarEntryFormat.Ustar)]
    [Arguments(TarEntryFormat.Pax)]
    [Arguments(TarEntryFormat.Gnu)]
    [Arguments(TarEntryFormat.V7)]
    public async Task PrepareTarAsync_SupportedTarFormats_PreserveRegularFiles(TarEntryFormat format)
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzipEntries(
                (CreateEntry(format, TarEntryType.RegularFile, "fix.sh"), "echo ok"),
                (CreateEntry(format, TarEntryType.RegularFile, "attachment"), "replacement"));
            var output = Path.Combine(root, "payload.tar");
            await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"), output, CancellationToken.None);

            await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "work/noctf/fix/attachment")))
                .IsEqualTo("replacement");
            await using var stream = File.OpenRead(output);
            using var reader = new TarReader(stream);
            var names = new List<string>();
            while (await reader.GetNextEntryAsync() is { } entry)
            {
                names.Add(entry.Name.Replace('\\', '/'));
                await Assert.That(entry.EntryType is TarEntryType.RegularFile or TarEntryType.Directory).IsTrue();
            }
            await Assert.That(names).Contains("noctf/fix/fix.sh");
            await Assert.That(names).Contains("noctf/fix/attachment");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_GnuDotDirectoryAndLongPath_AreNormalized()
    {
        var root = NewRoot();
        try
        {
            var nested = $"{new string('a', 60)}/{new string('b', 60)}.txt";
            await using var archive = TarGzipEntries(
                (new GnuTarEntry(TarEntryType.Directory, "./"), null),
                (new GnuTarEntry(TarEntryType.RegularFile, "./fix.sh"), "echo ok"),
                (new GnuTarEntry(TarEntryType.RegularFile, $"./{nested}"), "long-path-data"));
            await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "work/noctf/fix", nested)))
                .IsEqualTo("long-path-data");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments("../escape.sh")]
    [Arguments("/fix.sh")]
    [Arguments("directory/../fix.sh")]
    [Arguments(".\\fix.sh")]
    public async Task PrepareTarAsync_GnuUnsafePath_RemainsRejected(string path)
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzipEntries((new GnuTarEntry(TarEntryType.RegularFile, path), "bad"));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
            await Assert.That(File.Exists(Path.Combine(root, "escape.sh"))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments("./fix.sh")]
    [Arguments("./FIX.SH")]
    [Arguments(".//fix.sh")]
    public async Task PrepareTarAsync_GnuAliasedDuplicatePath_IsRejected(string duplicate)
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzipEntries(
                (new GnuTarEntry(TarEntryType.RegularFile, "fix.sh"), "first"),
                (new GnuTarEntry(TarEntryType.RegularFile, duplicate), "second"));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "work/noctf/fix/fix.sh")))
                .IsEqualTo("first");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments(TarEntryType.SymbolicLink)]
    [Arguments(TarEntryType.HardLink)]
    [Arguments(TarEntryType.Fifo)]
    public async Task PrepareTarAsync_GnuNonRegularEntry_RemainsRejected(TarEntryType type)
    {
        var root = NewRoot();
        try
        {
            var entry = new GnuTarEntry(type, "unsafe");
            if (type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = "fix.sh";
            await using var archive = TarGzipEntries(
                (new GnuTarEntry(TarEntryType.RegularFile, "fix.sh"), "echo ok"), (entry, null));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments(1, 10000L, 10000L, 100d)]
    [Arguments(10, 5L, 10000L, 100d)]
    [Arguments(10, 10000L, 2L, 100d)]
    [Arguments(10, 10000L, 10000L, 1d)]
    public async Task PrepareTarAsync_GnuExtractionLimits_RemainEnforced(
        int maxEntries, long maxExpanded, long maxSingleFile, double maxRatio)
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzipEntries(
                (new GnuTarEntry(TarEntryType.RegularFile, "fix.sh"), "echo ok"),
                (new GnuTarEntry(TarEntryType.RegularFile, "attachment"), new string('a', 1000)));
            var preparer = new FixArchivePreparer(Options.Create(new FixVerificationOptions
            {
                MaxArchiveEntries = maxEntries,
                MaxExpandedBytes = maxExpanded,
                MaxSingleFileBytes = maxSingleFile,
                MaxCompressionRatio = maxRatio
            }));
            var action = async () => await preparer.PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_SafeTarGzip_WritesUnderFixedContainerDirectory()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip(("fix.sh", "echo ok"), ("files/app.txt", "patched"));
            var output = Path.Combine(root, "payload.tar");
            await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"), output, CancellationToken.None);

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
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
            await Assert.That(File.Exists(Path.Combine(root, "escape.sh"))).IsFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_AbsolutePath_IsRejected()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzipEntry(
                new PaxTarEntry(TarEntryType.RegularFile, "/fix.sh"),
                "echo bad");
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments(TarEntryType.SymbolicLink)]
    [Arguments(TarEntryType.HardLink)]
    public async Task PrepareTarAsync_LinkEntry_IsRejected(TarEntryType entryType)
    {
        var root = NewRoot();
        try
        {
            var link = new PaxTarEntry(entryType, "payload/link") { LinkName = "fix.sh" };
            await using var archive = TarGzipEntries(
                (new PaxTarEntry(TarEntryType.RegularFile, "fix.sh"), "echo ok"),
                (link, null));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_UncompressedTar_IsRejected()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarWithoutGzip(("fix.sh", "echo ok"));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
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
            var action = async () => await CreatePreparer().PrepareTarAsync(
                zip, "fix.zip", "fix.sh", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);
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
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"), Path.Combine(root, "payload.tar"), CancellationToken.None);
            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_RejectsEmptyArchive()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip();
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

            await Assert.That(action).Throws<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PrepareTarAsync_RequiresEntrypointAtExactArchivePath()
    {
        var root = NewRoot();
        try
        {
            await using var archive = TarGzip(("nested/fix.sh", "echo ok"));
            var action = async () => await CreatePreparer().PrepareTarAsync(
                archive, "fix.tar.gz", "fix.sh", Path.Combine(root, "work"),
                Path.Combine(root, "payload.tar"), CancellationToken.None);

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

    private static MemoryStream TarGzipEntry(PaxTarEntry entry, string? content) =>
        TarGzipEntries((entry, content));

    private static MemoryStream TarGzipEntries(params (TarEntry Entry, string? Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
            foreach (var (entry, content) in entries)
            {
                if (content is not null)
                    entry.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                tar.WriteEntry(entry);
            }
        stream.Position = 0;
        return stream;
    }

    private static TarEntry CreateEntry(TarEntryFormat format, TarEntryType type, string name) => format switch
    {
        TarEntryFormat.Ustar => new UstarTarEntry(type, name),
        TarEntryFormat.Pax => new PaxTarEntry(type, name),
        TarEntryFormat.Gnu => new GnuTarEntry(type, name),
        TarEntryFormat.V7 => new V7TarEntry(type == TarEntryType.RegularFile ? TarEntryType.V7RegularFile : type, name),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static MemoryStream TarWithoutGzip(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var tar = new TarWriter(stream, leaveOpen: true))
            foreach (var file in files)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Name)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(file.Content))
                });
        stream.Position = 0;
        return stream;
    }

    private static FixArchivePreparer CreatePreparer() =>
        new(Options.Create(new FixVerificationOptions()));

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"noctf-archive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
