using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Lifecycle;

namespace NoCTF.Application.Competitions.Management;

public sealed record CreateCompetitionCommand(
    string Title,
    string? Description,
    GameMode Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    Guid OwnerId,
    DateTimeOffset CreatedAt);

public sealed record CompetitionView(
    Guid Id,
    string Title,
    string? Description,
    GameMode Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatus Status,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    Guid OwnerId);

public sealed record UpdateCompetitionCommand(
    Guid CompetitionId,
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    Guid ActorId,
    DateTimeOffset UpdatedAt);

public interface ICompetitionManagementStore
{
    Task<CompetitionView> CreateAsync(CreateCompetitionCommand command, CancellationToken cancellationToken);
    Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken cancellationToken);
    Task<CompetitionView?> UpdateAsync(UpdateCompetitionCommand command, CancellationToken cancellationToken);
    Task<bool> SoftDeleteAsync(Guid competitionId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
}

public sealed class CreateCompetition(ICompetitionManagementStore store)
{
    public Task<OperationResult<CompetitionView>> ExecuteAsync(CreateCompetitionCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 160)
            return Task.FromResult(OperationResult<CompetitionView>.Failure("invalid_title", "Competition title is required and must be at most 160 characters."));
        if (command.MaxTeamMembers < 1)
            return Task.FromResult(OperationResult<CompetitionView>.Failure("invalid_team_size", "MaxTeamMembers must be greater than zero."));
        var schedule = CompetitionLifecyclePolicy.ValidateSchedule(command.StartTime, command.EndTime);
        if (!schedule.Succeeded)
            return Task.FromResult(OperationResult<CompetitionView>.Failure(schedule.ErrorCode!, schedule.ErrorMessage!));
        return ExecuteStoreAsync(command, cancellationToken);
    }

    private async Task<OperationResult<CompetitionView>> ExecuteStoreAsync(CreateCompetitionCommand command, CancellationToken ct) =>
        OperationResult<CompetitionView>.Success(await store.CreateAsync(command, ct));
}

public sealed class GetCompetition(ICompetitionManagementStore store)
{
    public Task<CompetitionView?> ExecuteAsync(Guid id, bool includeDraft, CancellationToken cancellationToken = default) =>
        store.FindAsync(id, includeDraft, cancellationToken);
}

public sealed class ListCompetitions(ICompetitionManagementStore store)
{
    public Task<IReadOnlyList<CompetitionView>> ExecuteAsync(bool includeDraft, CancellationToken cancellationToken = default) =>
        store.ListAsync(includeDraft, cancellationToken);
}

public sealed class UpdateCompetition(ICompetitionManagementStore store)
{
    public async Task<OperationResult<CompetitionView>> ExecuteAsync(UpdateCompetitionCommand command, CancellationToken ct = default)
    {
        var current = await store.FindAsync(command.CompetitionId, true, ct);
        if (current is null) return OperationResult<CompetitionView>.Failure("competition_not_found", "Competition was not found.");
        if (current.Status == CompetitionStatus.Finished)
            return OperationResult<CompetitionView>.Failure("competition_finished", "Finished competitions are read-only.");
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 160)
            return OperationResult<CompetitionView>.Failure("invalid_title", "Competition title is required and must be at most 160 characters.");
        if (command.MaxTeamMembers < 1)
            return OperationResult<CompetitionView>.Failure("invalid_team_size", "MaxTeamMembers must be greater than zero.");
        var schedule = CompetitionLifecyclePolicy.ValidateSchedule(command.StartTime, command.EndTime);
        if (!schedule.Succeeded)
            return OperationResult<CompetitionView>.Failure(schedule.ErrorCode!, schedule.ErrorMessage!);
        if (current.Status is CompetitionStatus.Running or CompetitionStatus.Paused
            && (command.StartTime != current.StartTime
                || command.TeamRegistrationAutoApprove != current.TeamRegistrationAutoApprove
                || command.MaxTeamMembers != current.MaxTeamMembers))
            return OperationResult<CompetitionView>.Failure("running_configuration_locked", "Running competitions cannot change start time or team registration rules.");
        var updated = await store.UpdateAsync(command, ct);
        return updated is null
            ? OperationResult<CompetitionView>.Failure("competition_conflict", "Competition status changed concurrently.")
            : OperationResult<CompetitionView>.Success(updated);
    }
}

public sealed class DeleteCompetition(ICompetitionManagementStore store)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, true, ct);
        if (current is null) return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (current.Status is CompetitionStatus.Running or CompetitionStatus.Paused)
            return OperationResult.Failure("competition_active", "An active competition cannot be deleted.");
        return await store.SoftDeleteAsync(competitionId, actorId, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("competition_conflict", "Competition changed concurrently.");
    }
}
