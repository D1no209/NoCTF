using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Teams.Registration;

public sealed record CreateTeamCommand(Guid CompetitionId, Guid UserId, string Name, string? AvatarUrl, DateTimeOffset RegisteredAt);
public sealed record TeamView(Guid Id, Guid CompetitionId, string Name, string? AvatarUrl, Guid CaptainId, TeamRegistrationStatus RegistrationStatus, bool IsLocked, DateTimeOffset RegisteredAt);
public sealed record TeamRegistrationPolicy(CompetitionStatus Status, bool AutoApprove, bool CompetitionDeleted);
public sealed record TeamCreateStoreResult(TeamView? Team, string? ErrorCode);
public sealed record UpdateTeamCommand(Guid CompetitionId, Guid TeamId, string Name, string? AvatarUrl);

public interface ITeamRegistrationStore
{
    Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken);
    Task<bool?> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken);
    Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken cancellationToken) =>
        Task.FromResult<TeamView?>(null);
    Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken);
    Task<TeamView?> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken);
    Task<string?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
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
        var created = await store.TryCreateAsync(command with { Name = name },
            policy.AutoApprove ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Pending, ct);
        return created.Team is not null
            ? OperationResult<TeamView>.Success(created.Team)
            : OperationResult<TeamView>.Failure(created.ErrorCode ?? "team_conflict", "The team could not be created.");
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

public sealed class UpdateTeam(ITeamRegistrationStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<TeamView>> ExecuteAsync(UpdateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView>.Failure("invalid_team_name", "Team name is required and must be at most 128 characters.");
        var updated = await store.UpdateAsync(command with { Name = name }, ct);
        if (updated is null)
            return OperationResult<TeamView>.Failure("team_not_found_or_locked", "Team was not found or can no longer be changed.");
        await cache.InvalidateAsync(command.CompetitionId, ct);
        await scheduler.EnqueueLeaderboardRefreshAsync(command.CompetitionId, ct);
        return OperationResult<TeamView>.Success(updated);
    }
}

public sealed class DeleteTeam(ITeamRegistrationStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var error = await store.SoftDeleteAsync(competitionId, teamId, actorId, now, ct);
        if (error is not null) return OperationResult.Failure(error, "Team was not deleted.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult.Success();
    }
}

public sealed class ReviewTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid teamId, bool approve, CancellationToken ct = default)
    {
        var changed = await store.SetStatusAsync(competitionId, teamId,
            approve ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Rejected, ct);
        return changed switch
        {
            null => OperationResult.Failure("team_not_found", "Team was not found."),
            false => OperationResult.Failure("team_review_conflict", "Team registration was already reviewed."),
            true => OperationResult.Success()
        };
    }
}
