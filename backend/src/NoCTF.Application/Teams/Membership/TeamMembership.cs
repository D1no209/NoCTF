using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Teams.Membership;

public sealed record InviteTeamMemberCommand(Guid CompetitionId, Guid TeamId, Guid InvitedUserId, Guid ActorId, DateTimeOffset Now, DateTimeOffset ExpiresAt);
public sealed record TeamInvitationView(Guid Id, Guid CompetitionId, Guid TeamId, Guid InvitedUserId, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public enum TeamMembershipFailure
{
    CompetitionNotFound,
    TeamNotFound,
    TeamForbidden,
    MembershipLocked,
    UserNotFound,
    UserAlreadyRegistered,
    TeamFull,
    InvitationConflict,
    InvitationNotFound,
    InvitationAlreadyAnswered,
    InvitationExpired,
    MembershipConflict,
    CaptainCannotBeRemoved,
    MemberNotFound,
    MembershipNotFound,
    CaptainMustTransfer,
    CaptainOnly
}

public sealed record InviteTeamMemberStoreResult(TeamInvitationView? Invitation, TeamMembershipFailure? Failure = null);

public interface ITeamMembershipStore
{
    Task<InviteTeamMemberStoreResult> InviteAsync(InviteTeamMemberCommand command, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> RespondAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken cancellationToken);
}

internal static class TeamMembershipFailureProtocol
{
    public static string Code(TeamMembershipFailure failure) => failure switch
    {
        TeamMembershipFailure.CompetitionNotFound => "competition_not_found",
        TeamMembershipFailure.TeamNotFound => "team_not_found",
        TeamMembershipFailure.TeamForbidden => "team_forbidden",
        TeamMembershipFailure.MembershipLocked => "membership_locked",
        TeamMembershipFailure.UserNotFound => "user_not_found",
        TeamMembershipFailure.UserAlreadyRegistered => "user_already_registered",
        TeamMembershipFailure.TeamFull => "team_full",
        TeamMembershipFailure.InvitationConflict => "invitation_conflict",
        TeamMembershipFailure.InvitationNotFound => "invitation_not_found",
        TeamMembershipFailure.InvitationAlreadyAnswered => "invitation_already_answered",
        TeamMembershipFailure.InvitationExpired => "invitation_expired",
        TeamMembershipFailure.MembershipConflict => "membership_conflict",
        TeamMembershipFailure.CaptainCannotBeRemoved => "captain_cannot_be_removed",
        TeamMembershipFailure.MemberNotFound => "member_not_found",
        TeamMembershipFailure.MembershipNotFound => "membership_not_found",
        TeamMembershipFailure.CaptainMustTransfer => "captain_must_transfer",
        TeamMembershipFailure.CaptainOnly => "captain_only",
        _ => "membership_rejected"
    };
}

public static class TeamMembershipPolicy
{
    public static bool IsMembershipChangeLocked(CompetitionStatus status) =>
        status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished;

    public static bool IsInvitationResponseLocked(CompetitionStatus status, bool accept) =>
        status == CompetitionStatus.Finished
        || accept && status is CompetitionStatus.Running or CompetitionStatus.Paused;
}

public sealed class RemoveTeamMember(ITeamMembershipStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken ct = default)
    {
        var failure = await store.RemoveMemberAsync(competitionId, teamId, targetUserId, actorId, ct);
        return failure is null ? OperationResult.Success() : OperationResult.Failure(TeamMembershipFailureProtocol.Code(failure.Value), "Team member was not removed.");
    }
}

public sealed class LeaveTeam(ITeamMembershipStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid userId, CancellationToken ct = default)
    {
        var failure = await store.LeaveAsync(competitionId, userId, ct);
        return failure is null ? OperationResult.Success() : OperationResult.Failure(TeamMembershipFailureProtocol.Code(failure.Value), "User could not leave the team.");
    }
}

public sealed class TransferTeamCaptain(ITeamMembershipStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken ct = default)
    {
        var failure = await store.TransferCaptainAsync(competitionId, teamId, actorId, newCaptainId, ct);
        return failure is null ? OperationResult.Success() : OperationResult.Failure(TeamMembershipFailureProtocol.Code(failure.Value), "Captain was not transferred.");
    }
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
            : OperationResult<TeamInvitationView>.Failure(
                TeamMembershipFailureProtocol.Code(result.Failure ?? TeamMembershipFailure.InvitationConflict),
                "Invitation was not created.");
    }
}

public sealed class RespondToTeamInvitation(ITeamMembershipStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid invitationId, Guid userId, bool accept, DateTimeOffset now, CancellationToken ct = default)
    {
        var failure = await store.RespondAsync(invitationId, userId, accept, now, ct);
        return failure is null ? OperationResult.Success() : OperationResult.Failure(TeamMembershipFailureProtocol.Code(failure.Value), "Invitation response was rejected.");
    }
}
