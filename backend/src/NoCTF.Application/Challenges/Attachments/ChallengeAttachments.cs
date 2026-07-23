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
    DateTimeOffset CreatedAt);

public sealed record ChallengeAttachmentContent(
    ChallengeAttachmentView Metadata,
    string ObjectKey);

public interface IChallengeAttachmentStore
{
    Task<IReadOnlyList<ChallengeAttachmentView>?> ListAdminAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<bool> AddAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid attachmentId,
        StoredObject storedObject,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<ChallengeAttachmentView?> UpdateAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
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
    IObjectStorage objects)
{
    public Task<IReadOnlyList<ChallengeAttachmentView>?> ListAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.ListAdminAsync(challengeId, actorId, isAdministrator, ct);

    public async Task<OperationResult<ChallengeAttachmentView>> UploadAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260)
            return OperationResult<ChallengeAttachmentView>.Failure("invalid_file_name", "FileName is invalid.");
        var attachmentId = Guid.CreateVersion7(now);
        var objectKey = $"challenges/{challengeId:N}/attachments/{attachmentId:N}";
        var stored = await objects.PutAsync(
            objectKey,
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            content,
            ct);
        var saved = await store.AddAsync(
            challengeId, actorId, isAdministrator, attachmentId, stored, now, ct);
        if (!saved)
        {
            await objects.DeleteAsync(objectKey, ct);
            return OperationResult<ChallengeAttachmentView>.Failure(
                "challenge_not_found",
                "Challenge was not found or access was denied.");
        }
        return OperationResult<ChallengeAttachmentView>.Success(new(
            attachmentId,
            challengeId,
            stored.FileName,
            stored.ContentType,
            stored.Length,
            stored.Sha256,
            now));
    }

    public async Task<OperationResult<ChallengeAttachmentView>> UpdateAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var result = await store.UpdateAsync(
            challengeId, attachmentId, actorId, isAdministrator,
            fileName.Trim(), contentType.Trim(), ct);
        return result is null
            ? OperationResult<ChallengeAttachmentView>.Failure(
                "attachment_not_found",
                "Attachment was not found or access was denied.")
            : OperationResult<ChallengeAttachmentView>.Success(result);
    }

    public async Task<OperationResult> DeleteAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.DeleteAsync(challengeId, attachmentId, actorId, isAdministrator, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("attachment_not_found", "Attachment was not found or access was denied.");
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
