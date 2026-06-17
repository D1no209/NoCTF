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
}
