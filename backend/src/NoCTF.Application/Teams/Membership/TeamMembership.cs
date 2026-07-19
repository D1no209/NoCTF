using NoCTF.Application.Common;

namespace NoCTF.Application.Teams.Membership;

public sealed record InviteTeamMemberCommand(Guid CompetitionId, Guid TeamId, Guid InvitedUserId, Guid ActorId, DateTimeOffset Now, DateTimeOffset ExpiresAt);
public sealed record TeamInvitationView(Guid Id, Guid CompetitionId, Guid TeamId, Guid InvitedUserId, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public interface ITeamMembershipStore
{
    Task<(TeamInvitationView? Invitation, string? Error)> InviteAsync(InviteTeamMemberCommand command, CancellationToken cancellationToken);
    Task<string?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class InviteTeamMember(ITeamMembershipStore store)
{
    public async Task<OperationResult<TeamInvitationView>> ExecuteAsync(InviteTeamMemberCommand command, CancellationToken ct = default)
    {
        if (command.ExpiresAt <= command.Now)
            return OperationResult<TeamInvitationView>.Failure("invalid_invitation_expiry", "Invitation expiry must be in the future.");
        var result = await store.InviteAsync(command, ct);
        return result.Invitation is not null
            ? OperationResult<TeamInvitationView>.Success(result.Invitation)
            : OperationResult<TeamInvitationView>.Failure(result.Error ?? "invitation_conflict", "Invitation was not created.");
    }
}

public sealed class RespondToTeamInvitation(ITeamMembershipStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken ct = default)
    {
        var error = await store.RespondAsync(invitationId, userId, accept, now, ct);
        return error is null ? OperationResult.Success() : OperationResult.Failure(error, "Invitation response was rejected.");
    }
}
