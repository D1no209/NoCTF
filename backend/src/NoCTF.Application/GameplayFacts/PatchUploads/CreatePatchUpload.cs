using System.Formats.Tar;
using System.IO.Compression;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Application.GameplayFacts.PatchUploads;

public sealed record PatchUploadScope(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid UserId,
    Guid RuntimeInstanceId,
    long MaximumArchiveBytes = PatchUploadRules.DefaultMaximumArchiveBytes);

public static class PatchUploadRules
{
    public const long DefaultMaximumArchiveBytes = 256L * 1024 * 1024;
    public const long HardMaximumArchiveBytes = 1024L * 1024 * 1024;
}

public sealed record AcceptedAwdpFix(Guid PatchUploadId, Guid GameplayFactId);

public enum PatchUploadFailureCode
{
    PatchUploadNotAvailable,
    ArchiveStreamNotSeekable,
    ArchiveTooLarge,
    ArchiveInvalid,
    DefenseTargetNotReady,
    AttemptsExhausted,
    DefenseTargetConsumed,
    PatchUploadConflict
}

public enum PatchUploadSaveState
{
    Accepted,
    DefenseTargetNotReady,
    DefenseTargetConsumed,
    AttemptsExhausted,
    ConcurrencyConflict
}

public sealed record PatchUploadSaveResult(
    PatchUploadSaveState State,
    Guid? GameplayFactId = null);

public interface IPatchUploadStore
{
    Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<PatchUploadSaveResult> SaveAsync(
        Guid patchUploadId,
        Guid gameplayFactId,
        PatchUploadScope scope,
        Guid fileId,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken);
}

public sealed class CreatePatchUpload(
    IPatchUploadStore store,
    ManagedFileUploads uploads)
{
    private const int MaxEntries = 10_000;
    private const long MaxExpandedBytes = 1L << 30;
    private const long MaxSingleFileBytes = 256L << 20;

    public async Task<OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var scope = await store.ResolveScopeAsync(
            competitionId, competitionChallengeId, runtimeInstanceId, userId, ct);
        if (scope is null)
            return OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.PatchUploadNotAvailable,
                "No ready one-shot defense target is available for this team and challenge.");
        if (!content.CanSeek)
            return OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveStreamNotSeekable,
                "The archive stream must support validation before storage.");
        if (content.Length > scope.MaximumArchiveBytes)
            return OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveTooLarge,
                $"The archive exceeds the configured {scope.MaximumArchiveBytes}-byte upload limit.");

        var validation = ValidateArchive(content);
        content.Position = 0;
        if (validation is not null)
            return OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveInvalid, validation);

        var id = Guid.CreateVersion7(now);
        var gameplayFactId = Guid.CreateVersion7(now.AddTicks(1));
        var fileId = Guid.CreateVersion7(now);
        var uploaded = await uploads.CreateAsync(
            fileId,
            $"fix-uploads/{id:N}",
            Path.GetFileName(fileName),
            string.IsNullOrWhiteSpace(contentType)
                ? "application/gzip"
                : contentType,
            content,
            now,
            ct);
        PatchUploadSaveResult? saved = null;
        try
        {
            saved = await store.SaveAsync(
                id,
                gameplayFactId,
                scope,
                uploaded.FileId,
                now,
                ct);
        }
        finally
        {
            if (saved?.State != PatchUploadSaveState.Accepted)
                await uploads.AbandonAsync(uploaded.FileId);
        }
        return saved!.State switch
        {
            PatchUploadSaveState.Accepted =>
                OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Success(
                    new(id, saved.GameplayFactId!.Value)),
            PatchUploadSaveState.DefenseTargetNotReady =>
                OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.DefenseTargetNotReady,
                    "The one-shot defense target is no longer ready for a patch."),
            PatchUploadSaveState.AttemptsExhausted =>
                OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.AttemptsExhausted,
                    "The maximum number of Fix attempts has been reached."),
            PatchUploadSaveState.DefenseTargetConsumed =>
                OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.DefenseTargetConsumed,
                    "The one-shot defense target already consumed its only patch."),
            _ => OperationResult<AcceptedAwdpFix, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.PatchUploadConflict,
                "The one-shot defense target was consumed concurrently.")
        };
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
