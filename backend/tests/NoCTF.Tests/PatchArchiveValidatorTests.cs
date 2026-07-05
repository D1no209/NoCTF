using System.Formats.Tar;
using System.IO.Compression;
using NoCTF.Application.Security;

namespace NoCTF.Tests;

public class PatchArchiveValidatorTests
{
    [Fact]
    public async Task ValidateAsync_NormalTarGz_IsValid()
    {
        await using var archive = CreateTarGz(writer =>
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "patch.sh")
            {
                DataStream = new MemoryStream("echo ok"u8.ToArray())
            };
            writer.WriteEntry(entry);
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsPathTraversal()
    {
        await using var archive = CreateTarGz(writer =>
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../patch.sh")
            {
                DataStream = new MemoryStream("echo bad"u8.ToArray())
            };
            writer.WriteEntry(entry);
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsSymlink()
    {
        await using var archive = CreateTarGz(writer =>
        {
            var entry = new PaxTarEntry(TarEntryType.SymbolicLink, "patch.sh")
            {
                LinkName = "/etc/passwd"
            };
            writer.WriteEntry(entry);
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsExpandedArchiveOverLimit()
    {
        await using var archive = CreateTarGz(writer =>
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "patch.bin")
            {
                DataStream = new MemoryStream(new byte[(20 * 1024 * 1024) + 1])
            };
            writer.WriteEntry(entry);
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive);

        Assert.False(result.IsValid);
        Assert.Contains("expands", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsZipSymlink()
    {
        await using var archive = CreateZip(zip =>
        {
            var entry = zip.CreateEntry("fix.sh");
            entry.ExternalAttributes = unchecked((int)0xA0000000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("/etc/passwd");
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive, "patch.zip", "fix.sh");

        Assert.False(result.IsValid);
        Assert.Contains("unsupported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsTooManyEntries()
    {
        await using var archive = CreateTarGz(writer =>
        {
            for (var i = 0; i < 257; i++)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"patch-{i}.sh")
                {
                    DataStream = new MemoryStream("echo ok"u8.ToArray())
                };
                writer.WriteEntry(entry);
            }
        });

        var result = await new PatchArchiveValidator().ValidateAsync(archive);

        Assert.False(result.IsValid);
        Assert.Contains("too many", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream CreateTarGz(Action<TarWriter> write)
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, leaveOpen: true))
        {
            write(writer);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateZip(Action<ZipArchive> write)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            write(zip);
        }

        stream.Position = 0;
        return stream;
    }
}
