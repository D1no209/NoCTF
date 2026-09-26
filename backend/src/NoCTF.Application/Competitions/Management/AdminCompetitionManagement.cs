namespace NoCTF.Application.Competitions.Management;

public enum CompetitionRestoreState
{
    Restored,
    NotFound,
    UserNotFound,
    RoleNotEligible
}

public sealed record CompetitionRestoreResult(
    CompetitionRestoreState State,
    IReadOnlyList<Guid>? UserIds = null);

public enum CompetitionOwnerTransferState
{
    Transferred,
    InvalidOwnerId,
    NotFound,
    UserNotFound,
    RoleNotEligible
}

public enum CompetitionHardDeleteReferenceKind
{
    HistoricalEvent,
    Team,
    CompetitionChallenge,
    GameplayFact,
    RuntimeInstance,
    PatchUpload,
    Notification,
    PosterFile,
    TeamWriteUp,
    ActiveRuntimeResource,
    NotificationScopeConflict,
    ProgressionGraph,
    Badge,
    BadgeGrant
}

public sealed record CompetitionHardDeleteReference(
    CompetitionHardDeleteReferenceKind Kind,
    int Count);

public sealed record CompetitionHardDeletePreview(
    Guid CompetitionId,
    string Title,
    bool IsSoftDeleted,
    bool CanHardDelete,
    bool CanForceDelete,
    IReadOnlyList<CompetitionHardDeleteReference> References);

public enum CompetitionHardDeleteState
{
    Deleted,
    NotFound,
    Blocked
}

public sealed record CompetitionHardDeleteResult(
    CompetitionHardDeleteState State,
    CompetitionHardDeletePreview? Preview = null);

public sealed record ForceDeleteCompetitionCommand(
    Guid CompetitionId,
    Guid ActorId,
    string ConfirmationTitle,
    string Reason,
    DateTimeOffset OccurredAt);

public enum CompetitionForceDeleteState
{
    Deleted,
    NotFound,
    ActiveCompetition,
    ActiveRuntimeResource,
    ConfirmationMismatch,
    InvalidReason,
    NotificationScopeConflict
}

public sealed record CompetitionForceDeleteResult(
    CompetitionForceDeleteState State,
    CompetitionHardDeletePreview? Preview = null,
    IReadOnlyList<Guid>? ConflictingNotificationIds = null);

public sealed record CompetitionForceDeletionFact(
    int SchemaVersion,
    Guid CompetitionId,
    string CompetitionTitle,
    string Reason,
    IReadOnlyList<CompetitionHardDeleteReference> DeletedReferences);

public sealed record CompetitionOwnerTransferResult(
    CompetitionOwnerTransferState State,
    CompetitionView? Competition = null,
    IReadOnlyList<Guid>? UserIds = null);

public interface IAdminCompetitionStore
{
    Task<IReadOnlyList<CompetitionView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<CompetitionView?> FindAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken cancellationToken);
    Task<CompetitionRestoreResult> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<CompetitionHardDeletePreview?> PreviewHardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionHardDeleteResult> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionForceDeleteResult> ForceDeleteAsync(
        ForceDeleteCompetitionCommand command,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionOwnerTransferResult> TransferOwnerAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ListAdminCompetitions(IAdminCompetitionStore store)
{
    public Task<IReadOnlyList<CompetitionView>> ExecuteAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct = default) =>
        store.ListAsync(actorId, isAdministrator, includeDeleted, ct);
}

public sealed class GetAdminCompetition(IAdminCompetitionStore store)
{
    public Task<CompetitionView?> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, actorId, isAdministrator, includeDeleted, ct);
}

public sealed class RestoreCompetition(IAdminCompetitionStore store)
{
    public Task<CompetitionRestoreResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.RestoreAsync(competitionId, actorId, isAdministrator, now, ct);
}

public sealed class HardDeleteCompetition(IAdminCompetitionStore store)
{
    public Task<CompetitionHardDeleteResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.HardDeleteAsync(competitionId, actorId, isAdministrator, ct);
}

public sealed class PreviewCompetitionHardDelete(IAdminCompetitionStore store)
{
    public Task<CompetitionHardDeletePreview?> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.PreviewHardDeleteAsync(competitionId, actorId, isAdministrator, ct);
}

public sealed class ForceDeleteCompetition(IAdminCompetitionStore store)
{
    public Task<CompetitionForceDeleteResult> ExecuteAsync(
        ForceDeleteCompetitionCommand command,
        bool isAdministrator,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Trim().Length is < 8 or > 500)
        {
            return Task.FromResult(new CompetitionForceDeleteResult(
                CompetitionForceDeleteState.InvalidReason));
        }

        if (string.IsNullOrEmpty(command.ConfirmationTitle))
        {
            return Task.FromResult(new CompetitionForceDeleteResult(
                CompetitionForceDeleteState.ConfirmationMismatch));
        }

        return store.ForceDeleteAsync(
            command with { Reason = command.Reason.Trim() },
            isAdministrator,
            ct);
    }
}

public sealed class TransferCompetitionOwner(IAdminCompetitionStore store)
{
    public Task<CompetitionOwnerTransferResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty)
        {
            return Task.FromResult(new CompetitionOwnerTransferResult(
                CompetitionOwnerTransferState.InvalidOwnerId));
        }
        return store.TransferOwnerAsync(
            competitionId,
            actorId,
            isAdministrator,
            ownerId,
            now,
            ct);
    }
}
