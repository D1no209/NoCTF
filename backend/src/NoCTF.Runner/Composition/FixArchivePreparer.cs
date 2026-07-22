using System.Formats.Tar;
using System.IO.Compression;

namespace NoCTF.Runner.Composition;

public sealed class FixArchivePreparer(IConfiguration configuration)
{
    private readonly long maxExpandedBytes = configuration.GetValue("FixVerification:MaxExpandedBytes", 268_435_456L);
    private readonly int maxEntries = configuration.GetValue("FixVerification:MaxArchiveEntries", 2048);

    public async Task PrepareTarAsync(Stream source, string fileName, string workDirectory, string tarPath,
        CancellationToken cancellationToken)
    {
        var payloadRoot = Path.Combine(workDirectory, "noctf", "fix");
        Directory.CreateDirectory(payloadRoot);
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            await ExtractZipAsync(source, payloadRoot, cancellationToken);
        else
            await ExtractTarAsync(source, fileName, payloadRoot, cancellationToken);
        TarFile.CreateFromDirectory(workDirectory, tarPath, includeBaseDirectory: false);
    }

    private async Task ExtractZipAsync(Stream source, string root, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > maxEntries) throw new InvalidDataException("Fix archive contains too many entries.");
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Fix archive links are forbidden.");
            var path = SafePath(root, entry.FullName);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            expanded = checked(expanded + entry.Length);
            if (expanded > maxExpandedBytes) throw new InvalidDataException("Fix archive expands beyond the configured limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = File.Create(path);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private async Task ExtractTarAsync(Stream source, string fileName, string root, CancellationToken cancellationToken)
    {
        Stream payload = source;
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            payload = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        await using var disposablePayload = payload == source ? null : payload;
        using var reader = new TarReader(payload, leaveOpen: true);
        long expanded = 0;
        var count = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (++count > maxEntries) throw new InvalidDataException("Fix archive contains too many entries.");
            var path = SafePath(root, entry.Name);
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.V7RegularFile)
                throw new InvalidDataException("Fix archive contains a forbidden entry type.");
            expanded = checked(expanded + entry.Length);
            if (expanded > maxExpandedBytes) throw new InvalidDataException("Fix archive expands beyond the configured limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = File.Create(path);
            if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
    }

    private static string SafePath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains('\\'))
            throw new InvalidDataException("Fix archive path is invalid.");
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fix archive path traversal is forbidden.");
        return path;
    }
}
