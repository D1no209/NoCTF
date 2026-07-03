using System.Formats.Tar;
using System.IO.Compression;

namespace NoCTF.Application.Security;

public interface IPatchArchiveValidator
{
    Task<PatchArchiveValidationResult> ValidateAsync(Stream archive, CancellationToken ct = default);
    Task<PatchArchiveValidationResult> ValidateAsync(
        Stream archive,
        string fileName,
        string? requiredEntry,
        CancellationToken ct = default);
}

public record PatchArchiveValidationResult(bool IsValid, string? Error)
{
    public static PatchArchiveValidationResult Valid() => new(true, null);
    public static PatchArchiveValidationResult Invalid(string error) => new(false, error);
}

public class PatchArchiveValidator : IPatchArchiveValidator
{
    public async Task<PatchArchiveValidationResult> ValidateAsync(Stream archive, CancellationToken ct = default)
        => await ValidateTarGzAsync(archive, requiredEntry: null, ct);

    public async Task<PatchArchiveValidationResult> ValidateAsync(
        Stream archive,
        string fileName,
        string? requiredEntry,
        CancellationToken ct = default)
    {
        var normalizedEntry = NormalizePath(requiredEntry);
        if (requiredEntry is not null && normalizedEntry is null)
            return PatchArchiveValidationResult.Invalid($"Fix entry '{requiredEntry}' is unsafe.");

        try
        {
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return await ValidateZipAsync(archive, normalizedEntry, ct);

            if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                return await ValidateTarGzAsync(archive, normalizedEntry, ct);
        }
        catch (InvalidDataException)
        {
            return PatchArchiveValidationResult.Invalid("Archive cannot be read.");
        }

        return PatchArchiveValidationResult.Invalid("Archive extension is not supported.");
    }

    private static async Task<PatchArchiveValidationResult> ValidateTarGzAsync(
        Stream archive,
        string? requiredEntry,
        CancellationToken ct)
    {
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new TarReader(gzip, leaveOpen: true);

        TarEntry? entry;
        var hasRequiredEntry = requiredEntry is null;
        while ((entry = await reader.GetNextEntryAsync(copyData: false, ct)) is not null)
        {
            var normalizedName = NormalizePath(entry.Name);
            if (normalizedName is null)
                return PatchArchiveValidationResult.Invalid($"Archive contains unsafe path '{entry.Name}'.");

            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink or
                TarEntryType.BlockDevice or TarEntryType.CharacterDevice or TarEntryType.Fifo)
            {
                return PatchArchiveValidationResult.Invalid($"Archive contains unsupported entry '{entry.Name}'.");
            }

            if (IsRegularFile(entry.EntryType) && string.Equals(normalizedName, requiredEntry, StringComparison.Ordinal))
                hasRequiredEntry = true;
        }

        return hasRequiredEntry
            ? PatchArchiveValidationResult.Valid()
            : PatchArchiveValidationResult.Invalid($"Archive is missing fix entry '{requiredEntry}'.");
    }

    private static async Task<PatchArchiveValidationResult> ValidateZipAsync(
        Stream archive,
        string? requiredEntry,
        CancellationToken ct)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var hasRequiredEntry = requiredEntry is null;

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;

            var normalizedName = NormalizePath(entry.FullName);
            if (normalizedName is null)
                return PatchArchiveValidationResult.Invalid($"Archive contains unsafe path '{entry.FullName}'.");

            if (string.Equals(normalizedName, requiredEntry, StringComparison.Ordinal))
                hasRequiredEntry = true;
        }

        await Task.CompletedTask;
        return hasRequiredEntry
            ? PatchArchiveValidationResult.Valid()
            : PatchArchiveValidationResult.Invalid($"Archive is missing fix entry '{requiredEntry}'.");
    }

    private static bool IsRegularFile(TarEntryType entryType)
        => entryType is TarEntryType.RegularFile
            or TarEntryType.V7RegularFile
            or TarEntryType.ContiguousFile;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal) ||
            normalized == "..")
        {
            return null;
        }

        return normalized;
    }
}
