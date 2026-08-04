using NoCTF.Application.Storage;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration.UserAccounts;

public enum UserDeletionReferenceKind
{
    CompetitionOwner,
    CompetitionCollaborator,
    ChallengeOwner,
    ChallengeManager,
    TeamCaptain,
    TeamMember,
    Submission,
    PatchUpload,
    Notification,
    ScoringEvent,
    CompetitionLifecycleAudit,
    UserAccountLifecycleAudit
}

public sealed record UserDeletionReference(
    UserDeletionReferenceKind Kind,
    int Count);

public sealed record UserDeletionPreview(
    Guid UserId,
    string UserName,
    UserAccountStatus AccountStatus,
    bool CanHardDelete,
    bool CanAnonymize,
    bool SelfDeletionForbidden,
    bool LastAdministratorProtected,
    IReadOnlyList<UserDeletionReference> References);

public enum UserDeletionMode
{
    HardDelete,
    Anonymize
}

public enum UserDeletionState
{
    PhysicallyDeleted,
    Anonymized,
    UserNotFound,
    HardDeleteBlocked,
    SelfDeletionForbidden,
    LastAdministratorProtected,
    AlreadyAnonymized,
    ReasonInvalid
}

public sealed record UserDeletionStoreResult(
    UserDeletionState State,
    UserDeletionPreview? Preview = null,
    string? AvatarObjectKey = null);

public interface IUserAccountAdministrationStore
{
    Task<UserDeletionPreview?> PreviewDeletionAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<UserDeletionStoreResult> DeleteAsync(
        Guid userId,
        Guid actorUserId,
        UserDeletionMode mode,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageUserAccounts(
    IUserAccountAdministrationStore store,
    IObjectStorage objects)
{
    public const int MaximumReasonLength = 500;

    public Task<UserDeletionPreview?> PreviewDeletionAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.PreviewDeletionAsync(userId, actorUserId, ct);

    public async Task<UserDeletionStoreResult> DeleteAsync(
        Guid userId,
        Guid actorUserId,
        UserDeletionMode mode,
        string reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length is < 3 or > MaximumReasonLength)
            return new(UserDeletionState.ReasonInvalid);

        var result = await store.DeleteAsync(
            userId,
            actorUserId,
            mode,
            normalizedReason,
            now,
            ct);
        if (result.State is UserDeletionState.PhysicallyDeleted or UserDeletionState.Anonymized
            && !string.IsNullOrWhiteSpace(result.AvatarObjectKey))
        {
            try
            {
                await objects.DeleteAsync(result.AvatarObjectKey, CancellationToken.None);
            }
            catch
            {
                // Account state is authoritative; orphan cleanup remains best effort.
            }
        }

        return result;
    }
}
