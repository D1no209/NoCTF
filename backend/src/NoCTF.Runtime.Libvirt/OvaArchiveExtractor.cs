using System.Formats.Tar;

namespace NoCTF.Runtime.Libvirt;

public sealed record ExtractedOvaArchive(
    string RootDirectory,
    string DescriptorPath);

public static class OvaArchiveExtractor
{
    public static async Task<ExtractedOvaArchive> ExtractAsync(
        string ovaPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(destinationDirectory)
            && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            throw new InvalidOperationException("OVA extraction directory must be empty.");
        Directory.CreateDirectory(destinationDirectory);

        var root = Path.GetFullPath(destinationDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        string? descriptorPath = null;

        await using var archive = new FileStream(
            ovaPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new TarReader(archive, leaveOpen: false);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name)
                || entry.Name.Contains('\\', StringComparison.Ordinal))
                throw new InvalidOperationException("OVA contains an invalid archive path.");

            var relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!targetPath.StartsWith(rootPrefix, StringComparison.Ordinal)
                || !paths.Add(targetPath))
                throw new InvalidOperationException(
                    "OVA contains a duplicate path or escapes its extraction directory.");

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(targetPath);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(targetPath)
                        ?? throw new InvalidOperationException("OVA archive path is invalid."));
                    await using (var output = new FileStream(
                        targetPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 128,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        if (entry.DataStream is null)
                            throw new InvalidOperationException("OVA file entry has no content.");
                        await entry.DataStream.CopyToAsync(output, cancellationToken);
                    }
                    if (targetPath.EndsWith(".ovf", StringComparison.OrdinalIgnoreCase))
                    {
                        if (descriptorPath is not null)
                            throw new InvalidOperationException(
                                "OVA must contain exactly one OVF descriptor.");
                        descriptorPath = targetPath;
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"OVA archive entry type '{entry.EntryType}' is not supported.");
            }
        }

        if (descriptorPath is null)
            throw new InvalidOperationException("OVA does not contain an OVF descriptor.");
        return new(root, descriptorPath);
    }
}
