using System.Formats.Tar;
using System.IO.Compression;

namespace NoCTF.Application.Challenges.Capabilities;

public static class FixArchiveValidation
{
    private const int MaxEntries = 10_000;
    private const long MaxExpandedBytes = 1L << 30;
    private const long MaxSingleFileBytes = 256L << 20;

    public static string? Validate(Stream content)
    {
        try
        {
            using var gzip = new GZipStream(content, CompressionMode.Decompress, leaveOpen: true);
            using var tar = new TarReader(gzip, leaveOpen: true);
            var entries = 0;
            long totalLength = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (tar.GetNextEntry(copyData: false) is { } entry)
            {
                entries++;
                if (entries > MaxEntries)
                    return "The archive contains too many entries.";
                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink
                    or TarEntryType.BlockDevice or TarEntryType.CharacterDevice or TarEntryType.Fifo)
                    return "The archive contains an unsupported entry type.";
                if (Path.IsPathRooted(entry.Name)
                    || entry.Name.Split('/', '\\').Any(segment => segment == ".."))
                    return "The archive contains a path outside its extraction root.";
                var normalizedName = entry.Name.Replace('\\', '/').TrimEnd('/');
                if (!names.Add(normalizedName))
                    return "The archive contains duplicate paths.";
                if (entry.Length > MaxSingleFileBytes)
                    return "An archive entry exceeds the single-file limit.";
                totalLength = checked(totalLength + entry.Length);
                if (totalLength > MaxExpandedBytes)
                    return "The expanded archive exceeds the size limit.";
            }
            return entries == 0 ? "The archive is empty." : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or OverflowException)
        {
            return "The file is not a valid gzip-compressed tar archive.";
        }
    }
}
