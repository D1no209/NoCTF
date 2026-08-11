using NoCTF.Application.Challenges.Images;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Management;

public enum CompetitionRestoreState
{
    Restored,
    NotFound,
    UserNotFound,
    RoleNotEligible,
    ChallengeImageInvalid,
    RegistryAuthenticationRequired,
    RegistryAuthenticationFailed,
    RegistryUnavailable,
    RegistryManifestNotFound,
    RegistryManifestInvalid,
    ChallengeDefinitionRevisionConflict
}

public sealed record CompetitionRestoreResult(
    CompetitionRestoreState State,
    IReadOnlyList<Guid>? UserIds = null,
    string? Detail = null);

public enum CompetitionOwnerTransferState
{
    Transferred,
    InvalidOwnerId,
    NotFound,
    RevisionConflict,
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
    DataExport,
    Notification,
    PosterFile
}

public sealed record CompetitionHardDeleteReference(
    CompetitionHardDeleteReferenceKind Kind,
    int Count);

public sealed record CompetitionHardDeletePreview(
    Guid CompetitionId,
    string Title,
    bool IsSoftDeleted,
    bool CanHardDelete,
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
    Task<CompetitionRestoreResult> RestoreWithChallengeFenceAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
        CancellationToken cancellationToken) =>
        RestoreAsync(
            competitionId,
            actorId,
            isAdministrator,
            now,
            cancellationToken);
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

public sealed class RestoreCompetition(
    IAdminCompetitionStore store,
    PinChallengeImages? imagePinning = null)
{
    public async Task<CompetitionRestoreResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ChallengeImagePinResult? pinning = null;
        if (imagePinning is not null)
        {
            var competition = await store.FindAsync(
                competitionId,
                actorId,
                isAdministrator,
                includeDeleted: true,
                ct);
            if (competition is null || competition.DeletedAt is null)
                return new(CompetitionRestoreState.NotFound);
            if (competition.Status is CompetitionStatus.Published
                or CompetitionStatus.Running
                or CompetitionStatus.Paused)
            {
                pinning = await imagePinning.PinCompetitionAsync(
                    competitionId,
                    now,
                    ct,
                    includeDeleted: true);
                if (!pinning.Succeeded)
                {
                    var error = pinning.Errors[0];
                    return new(Map(error.Code), Detail: error.Message);
                }
            }
        }
        return pinning is null
            ? await store.RestoreAsync(
                competitionId,
                actorId,
                isAdministrator,
                now,
                ct)
            : await store.RestoreWithChallengeFenceAsync(
                competitionId,
                actorId,
                isAdministrator,
                now,
                pinning.ChallengeRevisions!,
                ct);
    }

    private static CompetitionRestoreState Map(ChallengeImagePinFailureCode failure) =>
        failure switch
        {
            ChallengeImagePinFailureCode.CompetitionNotFound
                or ChallengeImagePinFailureCode.ChallengeNotFound =>
                CompetitionRestoreState.NotFound,
            ChallengeImagePinFailureCode.RegistryAuthenticationRequired =>
                CompetitionRestoreState.RegistryAuthenticationRequired,
            ChallengeImagePinFailureCode.RegistryAuthenticationFailed =>
                CompetitionRestoreState.RegistryAuthenticationFailed,
            ChallengeImagePinFailureCode.RegistryUnavailable =>
                CompetitionRestoreState.RegistryUnavailable,
            ChallengeImagePinFailureCode.RegistryManifestNotFound =>
                CompetitionRestoreState.RegistryManifestNotFound,
            ChallengeImagePinFailureCode.RegistryManifestInvalid =>
                CompetitionRestoreState.RegistryManifestInvalid,
            ChallengeImagePinFailureCode.RevisionConflict =>
                CompetitionRestoreState.ChallengeDefinitionRevisionConflict,
            _ => CompetitionRestoreState.ChallengeImageInvalid
        };
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
