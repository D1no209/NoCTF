using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Registration;

public sealed record CreateTeamCommand(Guid CompetitionId, Guid UserId, string Name, string? AvatarUrl, DateTimeOffset RegisteredAt);
public sealed record TeamView(Guid Id, Guid CompetitionId, string Name, string? AvatarUrl, Guid CaptainId, TeamRegistrationStatus RegistrationStatus, bool IsLocked, DateTimeOffset RegisteredAt);
public sealed record TeamRegistrationPolicy(CompetitionStatus Status, bool AutoApprove, bool CompetitionDeleted);
public sealed record TeamCreateStoreResult(TeamView? Team, string? ErrorCode);

public interface ITeamRegistrationStore
{
    Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken);
    Task<bool?> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken);
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
