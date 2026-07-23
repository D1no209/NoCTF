using System.Formats.Tar;
using System.IO.Compression;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Application.Submissions.PatchUploads;

public sealed record PatchUploadScope(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid UserId);

public sealed record CreatedPatchUpload(Guid PatchUploadId);

public interface IPatchUploadStore
{
    Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<bool> SaveAsync(
        Guid patchUploadId,
        PatchUploadScope scope,
        string objectKey,
        string fileName,
        string contentType,
        long byteLength,
        byte[] sha256,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken);
}

public sealed class CreatePatchUpload(
    IPatchUploadStore store,
    IObjectStorage objects)
{
    private const int MaxEntries = 10_000;
    private const long MaxExpandedBytes = 1L << 30;
    private const long MaxSingleFileBytes = 256L << 20;

    public async Task<OperationResult<CreatedPatchUpload>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var scope = await store.ResolveScopeAsync(
            competitionId, competitionChallengeId, userId, ct);
        if (scope is null)
            return OperationResult<CreatedPatchUpload>.Failure(
                "patch_upload_not_available",
                "Patch upload is not available for this team and challenge.");
        if (!content.CanSeek)
            return OperationResult<CreatedPatchUpload>.Failure(
                "archive_stream_not_seekable",
                "The archive stream must support validation before storage.");

        var validation = ValidateArchive(content);
        content.Position = 0;
        if (validation is not null)
            return OperationResult<CreatedPatchUpload>.Failure(
                "archive_invalid", validation);

        var id = Guid.CreateVersion7(now);
        var objectKey = $"fix-uploads/{id:N}";
        var stored = await objects.PutAsync(
            objectKey,
            Path.GetFileName(fileName),
            string.IsNullOrWhiteSpace(contentType)
                ? "application/gzip"
                : contentType,
            content,
            ct);
        var saved = false;
        try
        {
            saved = await store.SaveAsync(
                id,
                scope,
                stored.ObjectKey,
                stored.FileName,
                stored.ContentType,
                stored.Length,
                Convert.FromHexString(stored.Sha256),
                now,
                ct);
        }
        finally
        {
            if (!saved)
                await objects.DeleteAsync(stored.ObjectKey, CancellationToken.None);
        }
        return saved
            ? OperationResult<CreatedPatchUpload>.Success(new(id))
            : OperationResult<CreatedPatchUpload>.Failure(
                "patch_upload_conflict",
                "This team already has an unconsumed patch upload.");
    }

    private static string? ValidateArchive(Stream content)
    {
        try
        {
            using var gzip = new GZipStream(
                content, CompressionMode.Decompress, leaveOpen: true);
            using var tar = new TarReader(gzip, leaveOpen: true);
            var entries = 0;
            long totalLength = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (tar.GetNextEntry(copyData: false) is { } entry)
            {
                entries++;
                if (entries > MaxEntries)
                    return "The archive contains too many entries.";
                if (entry.EntryType is TarEntryType.SymbolicLink
                    or TarEntryType.HardLink
                    or TarEntryType.BlockDevice
                    or TarEntryType.CharacterDevice
                    or TarEntryType.Fifo)
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
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or OverflowException)
        {
            return "The file is not a valid gzip-compressed tar archive.";
        }
    }
}
