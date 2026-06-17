using System.Formats.Tar;
using System.IO.Compression;

namespace NoCTF.Application.Security;

public interface IPatchArchiveValidator
{
    Task<PatchArchiveValidationResult> ValidateAsync(Stream archive, CancellationToken ct = default);
}

public record PatchArchiveValidationResult(bool IsValid, string? Error)
{
    public static PatchArchiveValidationResult Valid() => new(true, null);
    public static PatchArchiveValidationResult Invalid(string error) => new(false, error);
}

public class PatchArchiveValidator : IPatchArchiveValidator
{
    public async Task<PatchArchiveValidationResult> ValidateAsync(Stream archive, CancellationToken ct = default)
    {
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new TarReader(gzip, leaveOpen: true);

        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(copyData: false, ct)) is not null)
        {
            var name = entry.Name.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name))
                return PatchArchiveValidationResult.Invalid("Archive contains an empty path.");

            if (name.StartsWith("/", StringComparison.Ordinal) ||
                name.Contains("/../", StringComparison.Ordinal) ||
                name.StartsWith("../", StringComparison.Ordinal) ||
                name.EndsWith("/..", StringComparison.Ordinal))
            {
                return PatchArchiveValidationResult.Invalid($"Archive contains unsafe path '{entry.Name}'.");
            }

            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink or
                TarEntryType.BlockDevice or TarEntryType.CharacterDevice or TarEntryType.Fifo)
            {
                return PatchArchiveValidationResult.Invalid($"Archive contains unsupported entry '{entry.Name}'.");
            }
        }

        return PatchArchiveValidationResult.Valid();
    }
}
