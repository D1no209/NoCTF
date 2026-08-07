using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Teams.Membership;

public enum TeamMembershipFailure
{
    CompetitionNotFound,
    TeamNotFound,
    TeamForbidden,
    MembershipLocked,
    UserAlreadyRegistered,
    TeamFull,
    MembershipConflict,
    CaptainCannotBeRemoved,
    MemberNotFound,
    MembershipNotFound,
    CaptainMustTransfer,
    CaptainOnly
}

public interface ITeamMembershipStore
{
    Task<TeamMembershipFailure?> JoinByInvitationAsync(Guid competitionId, string invitationToken, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(Guid competitionId, Guid teamId, Guid actorId, string token, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> RemoveMemberAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> LeaveAsync(Guid competitionId, Guid userId, CancellationToken cancellationToken);
    Task<TeamMembershipFailure?> TransferCaptainAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken cancellationToken);
}

public sealed class JoinTeamByInvitation(ITeamMembershipStore store)
{
    public async Task<OperationResult<TeamMembershipFailure>> ExecuteAsync(Guid competitionId, string invitationToken, Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var failure = await store.JoinByInvitationAsync(competitionId, invitationToken, userId, now, ct);
        return failure is null ? OperationResult<TeamMembershipFailure>.Success() : OperationResult<TeamMembershipFailure>.Failure(failure.Value, "Team could not be joined.");
    }
}

public sealed class RotateTeamInvitation(ITeamMembershipStore store)
{
    public async Task<OperationResult<string, TeamMembershipFailure>> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorId,
        CancellationToken ct = default)
    {
        var token = InvitationTokenGenerator.Create();
        var result = await store.RotateInvitationAsync(competitionId, teamId, actorId, token, ct);
        return result.Token is not null ? OperationResult<string, TeamMembershipFailure>.Success(result.Token) : OperationResult<string, TeamMembershipFailure>.Failure(result.Failure!.Value, "Invitation could not be rotated.");
    }
}

public static class TeamMembershipPolicy
{
    public static bool IsMembershipChangeLocked(CompetitionStatus status) =>
        status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished;

}

public sealed class RemoveTeamMember(ITeamMembershipStore store)
{
    public async Task<OperationResult<TeamMembershipFailure>> ExecuteAsync(Guid competitionId, Guid teamId, Guid targetUserId, Guid actorId, CancellationToken ct = default)
    {
        var failure = await store.RemoveMemberAsync(competitionId, teamId, targetUserId, actorId, ct);
        return failure is null ? OperationResult<TeamMembershipFailure>.Success() : OperationResult<TeamMembershipFailure>.Failure(failure.Value, "Team member was not removed.");
    }
}

public sealed class LeaveTeam(ITeamMembershipStore store)
{
    public async Task<OperationResult<TeamMembershipFailure>> ExecuteAsync(Guid competitionId, Guid userId, CancellationToken ct = default)
    {
        var failure = await store.LeaveAsync(competitionId, userId, ct);
        return failure is null ? OperationResult<TeamMembershipFailure>.Success() : OperationResult<TeamMembershipFailure>.Failure(failure.Value, "User could not leave the team.");
    }
}

public sealed class TransferTeamCaptain(ITeamMembershipStore store)
{
    public async Task<OperationResult<TeamMembershipFailure>> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, Guid newCaptainId, CancellationToken ct = default)
    {
        var failure = await store.TransferCaptainAsync(competitionId, teamId, actorId, newCaptainId, ct);
        return failure is null ? OperationResult<TeamMembershipFailure>.Success() : OperationResult<TeamMembershipFailure>.Failure(failure.Value, "Captain was not transferred.");
    }
}

internal static class InvitationTokenGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Create()
    {
        Span<byte> random = stackalloc byte[64];
        Span<char> token = stackalloc char[32];
        var written = 0;
        while (written < token.Length)
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            foreach (var value in random)
            {
                if (value >= 248)
                    continue;
                token[written++] = Alphabet[value % Alphabet.Length];
                if (written == token.Length)
                    break;
            }
        }
        return new string(token);
    }
}
