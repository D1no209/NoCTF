namespace NoCTF.Application.Competitions.Permissions;

public sealed record UpdateCompetitionPermissionsCommand(
    Guid CompetitionId,
    Guid ActorId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

public interface ICompetitionPermissionStore
{
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
