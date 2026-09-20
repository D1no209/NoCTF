using NoCTF.Domain.Identity;

namespace NoCTF.Application.Competitions.Permissions;

public sealed record UpdateCompetitionPermissionsCommand(
    Guid CompetitionId,
    Guid ActorId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

public sealed record CompetitionPermissionSnapshot(
    Guid CompetitionId,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

public enum CompetitionPermissionSnapshotState
{
    Found,
    NotFound,
    Forbidden
}

public sealed record CompetitionPermissionSnapshotResult(
    CompetitionPermissionSnapshotState State,
    CompetitionPermissionSnapshot? Snapshot = null);

public sealed record CompetitionPermissionCandidate(
    Guid Id,
    string UserName,
    UserKind Kind,
    UserRole Role,
    bool EmailVerified);

public enum CompetitionPermissionCandidateListState
{
    Listed,
    NotFound,
    Forbidden
}

public sealed record CompetitionPermissionCandidateListResult(
    CompetitionPermissionCandidateListState State,
    IReadOnlyList<CompetitionPermissionCandidate>? Candidates = null);

public interface ICompetitionPermissionStore
{
    Task<CompetitionPermissionSnapshotResult> GetSnapshotAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionPermissionCandidateListResult> ListCandidatesAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken cancellationToken);
    Task<CompetitionPermissionUpdateResult> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken cancellationToken);
}

public enum CompetitionPermissionUpdateState
{
    Updated,
    NotFound,
    Forbidden,
    RolesOverlap,
    OwnerIncluded,
    UserNotFound,
    RoleNotEligible
}

public sealed record CompetitionPermissionUpdateResult(
    CompetitionPermissionUpdateState State,
    IReadOnlyList<Guid>? UserIds = null);

public sealed class UpdateCompetitionPermissions(ICompetitionPermissionStore store)
{
    public Task<CompetitionPermissionUpdateResult> ExecuteAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct = default)
    {
        var all = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .ToArray();
        if (all.Length != all.Distinct().Count())
        {
            return Task.FromResult(new CompetitionPermissionUpdateResult(
                CompetitionPermissionUpdateState.RolesOverlap));
        }
        return store.UpdateAsync(command, ct);
    }
}

public sealed class GetCompetitionPermissions(ICompetitionPermissionStore store)
{
    public Task<CompetitionPermissionSnapshotResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.GetSnapshotAsync(competitionId, actorId, isAdministrator, ct);
}

public sealed class ListCompetitionPermissionCandidates(ICompetitionPermissionStore store)
{
    public Task<CompetitionPermissionCandidateListResult> ExecuteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct = default) =>
        store.ListCandidatesAsync(competitionId, actorId, isAdministrator, ct);
}
