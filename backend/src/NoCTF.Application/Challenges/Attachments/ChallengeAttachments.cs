using NoCTF.Application.Common;
using NoCTF.Application.Storage;
using System.Text;

namespace NoCTF.Application.Challenges.Attachments;

public enum AttachmentDeliveryPolicy
{
    All,
    RandomOnePerTeam
}

public sealed record ChallengeAttachmentView(
    Guid Id,
    Guid ChallengeId,
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256,
    string? ExactFlag,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentSet(
    AttachmentDeliveryPolicy DeliveryPolicy,
    IReadOnlyList<ChallengeAttachmentView> Items);

public sealed record RandomAttachmentUploadItem(
    string OriginalFileName,
    string ContentType,
    Stream Content);

public sealed record RandomAttachmentBatchEntry(
    Guid AttachmentId,
    Guid FileId,
    string ExactFlag,
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentContent(
    ChallengeAttachmentView Metadata,
    string ObjectKey);

public enum AddChallengeAttachmentState
{
    Added,
    ChallengeNotFound,
    ResourceIdConflict,
    DeliveryModeConflict,
    DuplicateFlag
}

public enum ChallengeAttachmentFailureCode
{
    InvalidFileName,
    InvalidVariantFileName,
    EmptyBatch,
    DuplicateFlag,
    DeliveryModeConflict,
    BatchStorageFailed,
    BatchPersistenceFailed,
    ResourceIdConflict,
    ChallengeNotFound,
    AttachmentNotFound
}

public interface IChallengeAttachmentStore
{
    Task<bool> CanWriteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<ChallengeAttachmentSet?> ListAdminAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<AddChallengeAttachmentState> AddAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid attachmentId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AddChallengeAttachmentState> AddRandomBatchAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        string downloadFileName,
        IReadOnlyList<RandomAttachmentBatchEntry> entries,
        CancellationToken cancellationToken);
    Task<bool> AttachmentIdExistsAsync(
        Guid attachmentId,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> RestoreAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeAttachmentSet?> ListPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<ChallengeAttachmentContent?> GetPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? attachmentId,
        Guid userId,
        Func<string, CancellationToken, Task<bool>> objectExistsAsync,
        CancellationToken cancellationToken);
}

public sealed class ManageChallengeAttachments(
    IChallengeAttachmentStore store,
    ManagedFileUploads uploads)
{
    public Task<ChallengeAttachmentSet?> ListAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.ListAdminAsync(challengeId, actorId, isAdministrator, includeDeleted, ct);

    public async Task<OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>> UploadAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid? requestedAttachmentId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260)
            return OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>.Failure(
                ChallengeAttachmentFailureCode.InvalidFileName, "FileName is invalid.");
        if (!await store.CanWriteAsync(challengeId, actorId, isAdministrator, ct))
            return OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>.Failure(
                ChallengeAttachmentFailureCode.ChallengeNotFound,
                "Challenge was not found or access was denied.");
        var attachmentId = requestedAttachmentId ?? Guid.CreateVersion7(now);
        if (await store.AttachmentIdExistsAsync(attachmentId, ct))
            return OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>.Failure(
                ChallengeAttachmentFailureCode.ResourceIdConflict,
                "The requested attachment ID is already in use.");
        var fileId = Guid.CreateVersion7(now);
        var uploaded = await uploads.CreateAsync(
            fileId,
            $"attachments/{attachmentId:N}",
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            content,
            now,
            ct);
        AddChallengeAttachmentState saved;
        var added = false;
        try
        {
            saved = await store.AddAsync(
                challengeId,
                actorId,
                isAdministrator,
                attachmentId,
                uploaded.FileId,
                now,
                ct);
            added = saved == AddChallengeAttachmentState.Added;
        }
        finally
        {
            if (!added)
                await uploads.AbandonAsync(uploaded.FileId);
        }
        if (saved != AddChallengeAttachmentState.Added)
        {
            return OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>.Failure(
                saved == AddChallengeAttachmentState.ResourceIdConflict
                    ? ChallengeAttachmentFailureCode.ResourceIdConflict
                    : ChallengeAttachmentFailureCode.ChallengeNotFound,
                saved == AddChallengeAttachmentState.ResourceIdConflict
                    ? "The requested attachment ID is already in use."
                    : "Challenge was not found or access was denied.");
        }
        return OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>.Success(new(
            attachmentId,
            challengeId,
            uploaded.StoredObject.FileName,
            uploaded.StoredObject.ContentType,
            uploaded.StoredObject.Length,
            uploaded.StoredObject.Sha256,
            null,
            null,
            now));
    }

    public async Task<OperationResult<ChallengeAttachmentSet, ChallengeAttachmentFailureCode>>
        UploadRandomBatchAsync(
            Guid challengeId,
            Guid actorId,
            bool isAdministrator,
            string downloadFileName,
            IReadOnlyList<RandomAttachmentUploadItem> files,
            DateTimeOffset now,
            CancellationToken ct = default)
    {
        var normalizedDownloadName = NormalizeDownloadFileName(downloadFileName);
        if (normalizedDownloadName is null)
            return Failure(ChallengeAttachmentFailureCode.InvalidFileName,
                "The player download file name is invalid.");
        if (files.Count == 0)
            return Failure(ChallengeAttachmentFailureCode.EmptyBatch,
                "At least one attachment variant is required.");

        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!IsValidExactFlag(file.OriginalFileName))
                return Failure(ChallengeAttachmentFailureCode.InvalidVariantFileName,
                    "An attachment variant file name is not a valid exact Flag.");
            if (!flags.Add(file.OriginalFileName))
                return Failure(ChallengeAttachmentFailureCode.DuplicateFlag,
                    "Attachment variant Flags must be unique within a batch.");
        }
        if (!await store.CanWriteAsync(challengeId, actorId, isAdministrator, ct))
            return Failure(ChallengeAttachmentFailureCode.ChallengeNotFound,
                "Challenge was not found or access was denied.");

        var uploadedFiles = new List<ManagedFileUpload>(files.Count);
        var entries = new List<RandomAttachmentBatchEntry>(files.Count);
        try
        {
            foreach (var file in files)
            {
                var attachmentId = Guid.CreateVersion7();
                var fileId = Guid.CreateVersion7();
                var uploaded = await uploads.CreateAsync(
                    fileId,
                    $"attachments/{attachmentId:N}",
                    normalizedDownloadName,
                    string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    file.Content,
                    now,
                    ct);
                uploadedFiles.Add(uploaded);
                entries.Add(new RandomAttachmentBatchEntry(
                    attachmentId, uploaded.FileId, file.OriginalFileName, now));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await AbandonAllAsync(uploadedFiles);
            throw;
        }
        catch
        {
            await AbandonAllAsync(uploadedFiles);
            return Failure(ChallengeAttachmentFailureCode.BatchStorageFailed,
                "The attachment batch could not be stored.");
        }

        AddChallengeAttachmentState saved;
        try
        {
            saved = await store.AddRandomBatchAsync(
                challengeId,
                actorId,
                isAdministrator,
                normalizedDownloadName,
                entries,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await AbandonAllAsync(uploadedFiles);
            throw;
        }
        catch
        {
            await AbandonAllAsync(uploadedFiles);
            return Failure(ChallengeAttachmentFailureCode.BatchPersistenceFailed,
                "The attachment batch could not be persisted.");
        }

        if (saved != AddChallengeAttachmentState.Added)
        {
            await AbandonAllAsync(uploadedFiles);
            return saved switch
            {
                AddChallengeAttachmentState.ChallengeNotFound =>
                    Failure(ChallengeAttachmentFailureCode.ChallengeNotFound,
                        "Challenge was not found or access was denied."),
                AddChallengeAttachmentState.DeliveryModeConflict =>
                    Failure(ChallengeAttachmentFailureCode.DeliveryModeConflict,
                        "Ordinary attachments or static Flags cannot be mixed with random attachment variants."),
                AddChallengeAttachmentState.DuplicateFlag =>
                    Failure(ChallengeAttachmentFailureCode.DuplicateFlag,
                        "An attachment variant Flag is already in use."),
                _ => Failure(ChallengeAttachmentFailureCode.ResourceIdConflict,
                    "An attachment resource ID is already in use.")
            };
        }

        return OperationResult<ChallengeAttachmentSet, ChallengeAttachmentFailureCode>.Success(new(
            AttachmentDeliveryPolicy.RandomOnePerTeam,
            entries.Select((entry, index) => new ChallengeAttachmentView(
                entry.AttachmentId,
                challengeId,
                uploadedFiles[index].StoredObject.FileName,
                uploadedFiles[index].StoredObject.ContentType,
                uploadedFiles[index].StoredObject.Length,
                uploadedFiles[index].StoredObject.Sha256,
                entry.ExactFlag,
                null,
                entry.CreatedAt)).ToArray()));

        async Task AbandonAllAsync(IEnumerable<ManagedFileUpload> staged)
        {
            foreach (var uploaded in staged)
                await uploads.AbandonAsync(uploaded.FileId);
        }

        static OperationResult<ChallengeAttachmentSet, ChallengeAttachmentFailureCode> Failure(
            ChallengeAttachmentFailureCode code,
            string message) =>
            OperationResult<ChallengeAttachmentSet, ChallengeAttachmentFailureCode>.Failure(code, message);
    }

    public async Task<OperationResult<ChallengeAttachmentFailureCode>> DeleteAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.DeleteAsync(challengeId, attachmentId, actorId, isAdministrator, now, ct)
            ? OperationResult<ChallengeAttachmentFailureCode>.Success()
            : OperationResult<ChallengeAttachmentFailureCode>.Failure(
                ChallengeAttachmentFailureCode.AttachmentNotFound,
                "Attachment was not found or access was denied.");

    public async Task<OperationResult<ChallengeAttachmentFailureCode>> RestoreAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.RestoreAsync(
            challengeId,
            attachmentId,
            actorId,
            isAdministrator,
            now,
            ct)
            ? OperationResult<ChallengeAttachmentFailureCode>.Success()
            : OperationResult<ChallengeAttachmentFailureCode>.Failure(
                ChallengeAttachmentFailureCode.AttachmentNotFound,
                "Deleted attachment was not found or access was denied.");

    private static string? NormalizeDownloadFileName(string fileName)
    {
        var normalized = fileName.Trim();
        if (normalized.Length is < 1 or > 260
            || normalized != Path.GetFileName(normalized)
            || normalized.Any(char.IsControl))
            return null;
        return normalized;
    }

    private static bool IsValidExactFlag(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return bytes.Length is >= 1 and <= 4096
            && value != "."
            && value != ".."
            && value == Path.GetFileName(value)
            && !value.Any(char.IsControl)
            && !bytes.Contains((byte)0);
    }
}

public sealed class GetChallengeAttachments(
    IChallengeAttachmentStore store,
    IObjectStorage objects)
{
    public Task<ChallengeAttachmentSet?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct = default) =>
        store.ListPlayerAsync(competitionId, competitionChallengeId, userId, ct);

    public async Task<(ChallengeAttachmentView Metadata, Stream Content)?> OpenAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? attachmentId,
        Guid userId,
        CancellationToken ct = default)
    {
        var selected = await store.GetPlayerAsync(
            competitionId,
            competitionChallengeId,
            attachmentId,
            userId,
            async (objectKey, token) => await objects.InspectAsync(objectKey, token) is not null,
            ct);
        if (selected is null)
            return null;
        return (selected.Metadata, await objects.OpenReadAsync(selected.ObjectKey, ct));
    }
}
