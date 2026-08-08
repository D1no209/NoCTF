using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Registration;

public sealed record CreateTeamCommand(Guid CompetitionId, Guid UserId, string Name, DateTimeOffset RegisteredAt);
public sealed record TeamView(
    Guid Id,
    Guid CompetitionId,
    string Name,
    Guid? AvatarFileId,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatus RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt);
public sealed record TeamRegistrationPolicy(CompetitionStatus Status, bool AutoApprove, bool CompetitionDeleted);
public enum TeamRegistrationFailure
{
    InvalidTeamName,
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
public sealed record UpdateTeamCommand(Guid CompetitionId, Guid TeamId, string Name);

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

public sealed class CreateTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamView, TeamRegistrationFailure>> ExecuteAsync(CreateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.InvalidTeamName, "Team name is required and must be at most 128 characters.");
        var policy = await store.GetPolicyAsync(command.CompetitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (policy.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.RegistrationClosed, "Team registration is closed.");
        var created = await store.TryCreateAsync(command with { Name = name },
            policy.AutoApprove ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Pending, ct);
        return created.Team is not null
            ? OperationResult<TeamView, TeamRegistrationFailure>.Success(created.Team)
            : OperationResult<TeamView, TeamRegistrationFailure>.Failure(created.Failure ?? TeamRegistrationFailure.TeamConflict, "The team could not be created.");
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
    public async Task<OperationResult<TeamView, TeamRegistrationFailure>> ExecuteAsync(UpdateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.InvalidTeamName, "Team name is required and must be at most 128 characters.");
        var result = await store.UpdateAsync(command with { Name = name }, ct);
        if (result.Team is null)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(result.Failure ?? TeamRegistrationFailure.TeamConflict, "Team was not found or can no longer be changed.");
        return OperationResult<TeamView, TeamRegistrationFailure>.Success(result.Team);
    }
}

public sealed class DeleteTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var failure = await store.SoftDeleteAsync(competitionId, teamId, actorId, now, ct);
        if (failure is not null) return OperationResult<TeamRegistrationFailure>.Failure(failure.Value, "Team was not deleted.");
        return OperationResult<TeamRegistrationFailure>.Success();
    }
}

public sealed class ReviewTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(Guid competitionId, Guid teamId, bool approve, CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (policy.Status == CompetitionStatus.Finished)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionFinished, "Finished competitions are read-only.");
        var result = await store.SetStatusAsync(competitionId, teamId,
            approve ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Rejected, ct);
        return result.Changed
            ? OperationResult<TeamRegistrationFailure>.Success()
            : OperationResult<TeamRegistrationFailure>.Failure(result.Failure ?? TeamRegistrationFailure.TeamReviewConflict, "Team registration was not reviewed.");
    }
}

public sealed class ResubmitTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (policy.Status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.RegistrationClosed, "Team registration is closed.");
        var result = await store.ResubmitAsync(competitionId, teamId, userId, ct);
        return result.Changed
            ? OperationResult<TeamRegistrationFailure>.Success()
            : OperationResult<TeamRegistrationFailure>.Failure(
                result.Failure ?? TeamRegistrationFailure.TeamReviewConflict,
                "Rejected registration was not resubmitted.");
    }
}
