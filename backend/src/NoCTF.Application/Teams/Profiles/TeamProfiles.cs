using NoCTF.Application.Common;

namespace NoCTF.Application.Teams.Profiles;

public sealed record TeamProfileView(
    Guid Id,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    string InvitationToken,
    DateTimeOffset CreatedAt);

public sealed record CreateTeamProfileCommand(
    Guid UserId,
    string Name,
    string? AvatarUrl,
    DateTimeOffset CreatedAt);

public sealed record UpdateTeamProfileCommand(
    Guid TeamId,
    string Name,
    string? AvatarUrl);

public enum TeamProfileFailure
{
    TeamNotFound,
    TeamForbidden,
    TeamNameConflict,
    TeamConflict,
    UserAlreadyMember,
    MemberNotFound,
    CaptainCannotBeRemoved,
    CaptainMustTransfer,
    CaptainOnly
}

public sealed record TeamProfileMutationResult(
    TeamProfileView? Team = null,
    TeamProfileFailure? Failure = null);

public interface ITeamProfileStore
{
    Task<TeamProfileMutationResult> CreateAsync(
        CreateTeamProfileCommand command,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamProfileView>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
    Task<TeamProfileView?> FindAsync(
        Guid teamId,
        CancellationToken cancellationToken);
    Task<bool> CanManageAsync(
        Guid actorId,
        Guid teamId,
        CancellationToken cancellationToken);
    Task<TeamProfileMutationResult> UpdateAsync(
        UpdateTeamProfileCommand command,
        CancellationToken cancellationToken);
    Task<TeamProfileMutationResult> JoinAsync(
        string invitationToken,
        Guid userId,
        CancellationToken cancellationToken);
    Task<TeamProfileMutationResult> RotateInvitationAsync(
        Guid teamId,
        CancellationToken cancellationToken);
    Task<TeamProfileFailure?> RemoveMemberAsync(
        Guid teamId,
        Guid targetUserId,
        CancellationToken cancellationToken);
    Task<TeamProfileFailure?> LeaveAsync(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<TeamProfileFailure?> TransferCaptainAsync(
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken cancellationToken);
    Task<TeamProfileFailure?> SoftDeleteAsync(
        Guid teamId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken);
}

internal static class TeamProfileFailureProtocol
{
    public static string Code(TeamProfileFailure failure) => failure switch
    {
        TeamProfileFailure.TeamNotFound => "team_not_found",
        TeamProfileFailure.TeamForbidden => "team_forbidden",
        TeamProfileFailure.TeamNameConflict => "team_name_conflict",
        TeamProfileFailure.TeamConflict => "team_conflict",
        TeamProfileFailure.UserAlreadyMember => "user_already_member",
        TeamProfileFailure.MemberNotFound => "member_not_found",
        TeamProfileFailure.CaptainCannotBeRemoved => "captain_cannot_be_removed",
        TeamProfileFailure.CaptainMustTransfer => "captain_must_transfer",
        TeamProfileFailure.CaptainOnly => "captain_only",
        _ => "team_rejected"
    };
}

public sealed class CreateTeamProfile(ITeamProfileStore store)
{
    public async Task<OperationResult<TeamProfileView>> ExecuteAsync(
        CreateTeamProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
        {
            return OperationResult<TeamProfileView>.Failure(
                "invalid_team_name",
                "Team name is required and must be at most 128 characters.");
        }

        var result = await store.CreateAsync(
            command with { Name = name },
            cancellationToken);
        return Map(result, "The team could not be created.");
    }

    internal static OperationResult<TeamProfileView> Map(
        TeamProfileMutationResult result,
        string message) =>
        result.Team is not null
            ? OperationResult<TeamProfileView>.Success(result.Team)
            : OperationResult<TeamProfileView>.Failure(
                TeamProfileFailureProtocol.Code(
                    result.Failure ?? TeamProfileFailure.TeamConflict),
                message);
}

public sealed class ListMyTeamProfiles(ITeamProfileStore store)
{
    public Task<IReadOnlyList<TeamProfileView>> ExecuteAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        store.ListForUserAsync(userId, cancellationToken);
}

public sealed class UpdateTeamProfile(ITeamProfileStore store)
{
    public async Task<OperationResult<TeamProfileView>> ExecuteAsync(
        UpdateTeamProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
        {
            return OperationResult<TeamProfileView>.Failure(
                "invalid_team_name",
                "Team name is required and must be at most 128 characters.");
        }

        var result = await store.UpdateAsync(
            command with { Name = name },
            cancellationToken);
        return CreateTeamProfile.Map(result, "The team could not be updated.");
    }
}

public sealed class JoinTeamProfile(ITeamProfileStore store)
{
    public async Task<OperationResult<TeamProfileView>> ExecuteAsync(
        string invitationToken,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var token = invitationToken.Trim();
        if (token.Length != 32)
        {
            return OperationResult<TeamProfileView>.Failure(
                "invalid_invitation_token",
                "Invitation token must contain exactly 32 characters.");
        }

        var result = await store.JoinAsync(token, userId, cancellationToken);
        return CreateTeamProfile.Map(result, "The team could not be joined.");
    }
}

public sealed class RotateTeamProfileInvitation(ITeamProfileStore store)
{
    public async Task<OperationResult<string>> ExecuteAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        var result = await store.RotateInvitationAsync(teamId, cancellationToken);
        return result.Team is not null
            ? OperationResult<string>.Success(result.Team.InvitationToken)
            : OperationResult<string>.Failure(
                TeamProfileFailureProtocol.Code(
                    result.Failure ?? TeamProfileFailure.TeamConflict),
                "The invitation token could not be rotated.");
    }
}

public sealed class RemoveTeamProfileMember(ITeamProfileStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid teamId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var failure = await store.RemoveMemberAsync(
            teamId,
            targetUserId,
            cancellationToken);
        return failure is null
            ? OperationResult.Success()
            : OperationResult.Failure(
                TeamProfileFailureProtocol.Code(failure.Value),
                "The team member could not be removed.");
    }
}

public sealed class LeaveTeamProfile(ITeamProfileStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var failure = await store.LeaveAsync(teamId, userId, cancellationToken);
        return failure is null
            ? OperationResult.Success()
            : OperationResult.Failure(
                TeamProfileFailureProtocol.Code(failure.Value),
                "The team could not be left.");
    }
}

public sealed class TransferTeamProfileCaptain(ITeamProfileStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid teamId,
        Guid actorId,
        Guid newCaptainId,
        CancellationToken cancellationToken = default)
    {
        var failure = await store.TransferCaptainAsync(
            teamId,
            actorId,
            newCaptainId,
            cancellationToken);
        return failure is null
            ? OperationResult.Success()
            : OperationResult.Failure(
                TeamProfileFailureProtocol.Code(failure.Value),
                "The team captain could not be transferred.");
    }
}

public sealed class DeleteTeamProfile(ITeamProfileStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid teamId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken = default)
    {
        var failure = await store.SoftDeleteAsync(
            teamId,
            deletedAt,
            cancellationToken);
        return failure is null
            ? OperationResult.Success()
            : OperationResult.Failure(
                TeamProfileFailureProtocol.Code(failure.Value),
                "The team could not be deleted.");
    }
}
