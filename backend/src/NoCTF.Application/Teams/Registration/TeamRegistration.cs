using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Registration;

public sealed record CreateTeamCommand(
    Guid CompetitionId,
    Guid UserId,
    string Name,
    string? AvatarUrl,
    DateTimeOffset RegisteredAt,
    Guid? TeamProfileId = null,
    Guid? CaptainId = null,
    IReadOnlyList<Guid>? MemberIds = null);
public sealed record TeamView(
    Guid Id,
    Guid CompetitionId,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatus RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt,
    Guid? TeamProfileId = null);
public sealed record TeamRegistrationPolicy(
    CompetitionStatus Status,
    bool AutoApprove,
    bool CompetitionDeleted,
    int MaxTeamMembers = int.MaxValue);
public enum TeamRegistrationFailure
{
    CompetitionNotFound,
    RegistrationClosed,
    UserAlreadyRegistered,
    TeamNameOrMembershipConflict,
    TeamNotFound,
    CompetitionFinished,
    TeamLocked,
    TeamConflict,
    TeamReviewConflict,
    CompetitionActive
}
public sealed record TeamCreateStoreResult(TeamView? Team, TeamRegistrationFailure? Failure = null);
public sealed record TeamReviewStoreResult(bool Changed, TeamRegistrationFailure? Failure = null);
public sealed record TeamUpdateStoreResult(TeamView? Team, TeamRegistrationFailure? Failure = null);
public sealed record UpdateTeamCommand(Guid CompetitionId, Guid TeamId, string Name, string? AvatarUrl);

public interface ITeamRegistrationStore
{
    Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken);
    Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<TeamReviewStoreResult> ResubmitAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TeamReviewStoreResult(
            false,
            TeamRegistrationFailure.TeamReviewConflict));
    Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken);
    Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken cancellationToken) =>
        Task.FromResult<TeamView?>(null);
    Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken);
    Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken);
    Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
}

internal static class TeamRegistrationFailureProtocol
{
    public static string Code(TeamRegistrationFailure failure) => failure switch
    {
        TeamRegistrationFailure.CompetitionNotFound => "competition_not_found",
        TeamRegistrationFailure.RegistrationClosed => "registration_closed",
        TeamRegistrationFailure.UserAlreadyRegistered => "user_already_registered",
        TeamRegistrationFailure.TeamNameOrMembershipConflict => "team_name_or_membership_conflict",
        TeamRegistrationFailure.TeamNotFound => "team_not_found",
        TeamRegistrationFailure.CompetitionFinished => "competition_finished",
        TeamRegistrationFailure.TeamLocked => "team_locked",
        TeamRegistrationFailure.TeamConflict => "team_conflict",
        TeamRegistrationFailure.TeamReviewConflict => "team_review_conflict",
        TeamRegistrationFailure.CompetitionActive => "competition_active",
        _ => "team_rejected"
    };
}

public sealed class CreateTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamView>> ExecuteAsync(CreateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView>.Failure("invalid_team_name", "Team name is required and must be at most 128 characters.");
        var policy = await store.GetPolicyAsync(command.CompetitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamView>.Failure("competition_not_found", "Competition was not found.");
        if (policy.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult<TeamView>.Failure("registration_closed", "Team registration is closed.");
        var memberIds = (command.MemberIds ?? [command.UserId]).Distinct().ToArray();
        var captainId = command.CaptainId ?? command.UserId;
        if (memberIds.Length == 0 || !memberIds.Contains(captainId))
        {
            return OperationResult<TeamView>.Failure(
                "invalid_team_roster",
                "The team captain must be included in the registration roster.");
        }
        if (memberIds.Length > policy.MaxTeamMembers)
        {
            return OperationResult<TeamView>.Failure(
                "team_full",
                "The team has more members than this competition allows.");
        }
        var created = await store.TryCreateAsync(command with
        {
            Name = name,
            CaptainId = captainId,
            MemberIds = memberIds
        },
            policy.AutoApprove ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Pending, ct);
        return created.Team is not null
            ? OperationResult<TeamView>.Success(created.Team)
            : OperationResult<TeamView>.Failure(TeamRegistrationFailureProtocol.Code(created.Failure ?? TeamRegistrationFailure.TeamConflict), "The team could not be created.");
    }
}

public sealed class RegisterTeamProfile(
    NoCTF.Application.Teams.Profiles.ITeamProfileStore profiles,
    CreateTeam createTeam)
{
    public async Task<OperationResult<TeamView>> ExecuteAsync(
        Guid competitionId,
        Guid teamProfileId,
        Guid actorId,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken = default)
    {
        var profile = await profiles.FindAsync(teamProfileId, cancellationToken);
        if (profile is null)
        {
            return OperationResult<TeamView>.Failure(
                "team_not_found",
                "The team was not found.");
        }
        if (profile.CaptainId != actorId)
        {
            return OperationResult<TeamView>.Failure(
                "team_forbidden",
                "Only the team captain can register the team for a competition.");
        }
        return await createTeam.ExecuteAsync(new(
            competitionId,
            actorId,
            profile.Name,
            profile.AvatarUrl,
            registeredAt,
            profile.Id,
            profile.CaptainId,
            profile.MemberIds), cancellationToken);
    }
}

public sealed class ListCompetitionTeams(ITeamRegistrationStore store)
{
    public Task<IReadOnlyList<TeamView>> ExecuteAsync(Guid competitionId, bool includePending, CancellationToken ct = default) =>
        store.ListAsync(competitionId, includePending, ct);
}

public sealed class GetTeam(ITeamRegistrationStore store)
{
    public Task<TeamView?> ExecuteAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken ct = default) =>
        store.FindAsync(competitionId, teamId, includePending, ct);
}

public sealed class GetMyTeam(ITeamRegistrationStore store)
{
    public Task<TeamView?> ExecuteAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken ct = default) =>
        store.FindForUserAsync(competitionId, userId, includePending, ct);
}

public sealed class UpdateTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamView>> ExecuteAsync(UpdateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView>.Failure("invalid_team_name", "Team name is required and must be at most 128 characters.");
        var result = await store.UpdateAsync(command with { Name = name }, ct);
        if (result.Team is null)
            return OperationResult<TeamView>.Failure(TeamRegistrationFailureProtocol.Code(result.Failure ?? TeamRegistrationFailure.TeamConflict), "Team was not found or can no longer be changed.");
        return OperationResult<TeamView>.Success(result.Team);
    }
}

public sealed class DeleteTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var failure = await store.SoftDeleteAsync(competitionId, teamId, actorId, now, ct);
        if (failure is not null) return OperationResult.Failure(TeamRegistrationFailureProtocol.Code(failure.Value), "Team was not deleted.");
        return OperationResult.Success();
    }
}

public sealed class ReviewTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, bool approve, CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (policy.Status == CompetitionStatus.Finished)
            return OperationResult.Failure("competition_finished", "Finished competitions are read-only.");
        var result = await store.SetStatusAsync(competitionId, teamId,
            approve ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Rejected, ct);
        return result.Changed
            ? OperationResult.Success()
            : OperationResult.Failure(TeamRegistrationFailureProtocol.Code(result.Failure ?? TeamRegistrationFailure.TeamReviewConflict), "Team registration was not reviewed.");
    }
}

public sealed class ResubmitTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (policy.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult.Failure("registration_closed", "Team registration is closed.");
        var result = await store.ResubmitAsync(competitionId, teamId, userId, ct);
        return result.Changed
            ? OperationResult.Success()
            : OperationResult.Failure(
                TeamRegistrationFailureProtocol.Code(
                    result.Failure ?? TeamRegistrationFailure.TeamReviewConflict),
                "Rejected registration was not resubmitted.");
    }
}
