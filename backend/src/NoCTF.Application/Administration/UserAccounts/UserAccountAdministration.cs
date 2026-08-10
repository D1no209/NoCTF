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
    GameplayFact,
    PatchUpload,
    Notification,
    CompetitionEvent
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
    Guid? PreviousAvatarFileId = null);

public sealed record UserAccountLifecycleFact(
    int SchemaVersion,
    Guid TargetUserId,
    string TargetUserName,
    UserAccountLifecycleAction Action,
    string Reason,
    bool Automatic);

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

public sealed class ManageUserAccounts(IUserAccountAdministrationStore store)
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

        return await store.DeleteAsync(
            userId,
            actorUserId,
            mode,
            normalizedReason,
            now,
            ct);
    }
}
