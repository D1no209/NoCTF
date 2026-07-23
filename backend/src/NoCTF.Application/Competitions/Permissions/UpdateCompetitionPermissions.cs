using NoCTF.Application.Common;

namespace NoCTF.Application.Competitions.Permissions;

public sealed record UpdateCompetitionPermissionsCommand(
    Guid CompetitionId,
    Guid ActorId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

public interface ICompetitionPermissionStore
{
    Task<CompetitionPermissionUpdateState> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken cancellationToken);
}

public enum CompetitionPermissionUpdateState
{
    Updated,
    NotFound,
    Forbidden,
    InvalidUser
}

public sealed class UpdateCompetitionPermissions(ICompetitionPermissionStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct = default)
    {
        var all = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .ToArray();
        if (all.Length != all.Distinct().Count())
            return OperationResult.Failure(
                "permission_roles_overlap",
                "A user can have only one competition permission role.");

        var state = await store.UpdateAsync(command, ct);
        return state switch
        {
            CompetitionPermissionUpdateState.Updated => OperationResult.Success(),
            CompetitionPermissionUpdateState.NotFound => OperationResult.Failure(
                "competition_not_found", "Competition was not found."),
            CompetitionPermissionUpdateState.Forbidden => OperationResult.Failure(
                "competition_forbidden", "Only the owner or a platform administrator can update permissions."),
            _ => OperationResult.Failure(
                "permission_user_not_found", "At least one permission user was not found.")
        };
    }
}
