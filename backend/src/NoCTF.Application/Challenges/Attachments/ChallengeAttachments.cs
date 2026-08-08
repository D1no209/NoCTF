using NoCTF.Application.Common;
using NoCTF.Application.Storage;

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
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentContent(
    ChallengeAttachmentView Metadata,
    string ObjectKey);

public enum AddChallengeAttachmentState
{
    Added,
    ChallengeNotFound,
    ResourceIdConflict
}

public enum ChallengeAttachmentFailureCode
{
    InvalidFileName,
    ResourceIdConflict,
    ChallengeNotFound,
    AttachmentNotFound
}

public interface IChallengeAttachmentStore
{
    Task<IReadOnlyList<ChallengeAttachmentView>?> ListAdminAsync(
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
    Task<IReadOnlyList<ChallengeAttachmentView>?> ListPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<ChallengeAttachmentContent?> GetPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? attachmentId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed class ManageChallengeAttachments(
    IChallengeAttachmentStore store,
    ManagedFileUploads uploads)
{
    public Task<IReadOnlyList<ChallengeAttachmentView>?> ListAsync(
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
            now));
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
}

public sealed class GetChallengeAttachments(
    IChallengeAttachmentStore store,
    IObjectStorage objects)
{
    public Task<IReadOnlyList<ChallengeAttachmentView>?> ListAsync(
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
            competitionId, competitionChallengeId, attachmentId, userId, ct);
        if (selected is null)
            return null;
        return (selected.Metadata, await objects.OpenReadAsync(selected.ObjectKey, ct));
    }
}
