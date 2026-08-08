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
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    bool AllowTeamRegistrationWhileRunning = false);

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
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid OwnerId,
    CompetitionLeaderboardVisibility LeaderboardVisibility = CompetitionLeaderboardVisibility.Normal,
    DateTimeOffset? LeaderboardVisibilityStartsAt = null,
    bool AllowTeamRegistrationWhileRunning = false);

public enum CompetitionCreationState
{
    Created,
    InvalidRequest,
    UserNotFound,
    RoleNotEligible
}

public enum CompetitionManagementFailureCode
{
    CompetitionActive,
    CompetitionFinished,
    CompetitionNotFound,
    InvalidTitle,
    InvalidTeamSize,
    InvalidSchedule,
    CompetitionConflict
}

public sealed record CompetitionCreationResult(
    CompetitionCreationState State,
    CompetitionView? Competition = null,
    IReadOnlyList<Guid>? UserIds = null,
    string? Detail = null)
{
    public bool Succeeded => State == CompetitionCreationState.Created;
}

public sealed record UpdateCompetitionCommand(
    Guid CompetitionId,
    string Title,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid ActorId,
    DateTimeOffset UpdatedAt,
    bool AllowTeamRegistrationWhileRunning = false);

public interface ICompetitionManagementStore
{
    Task<CompetitionCreationResult> CreateAsync(
        CreateCompetitionCommand command,
        CancellationToken cancellationToken);
    Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken cancellationToken);
    Task<CompetitionView?> UpdateAsync(UpdateCompetitionCommand command, CompetitionStatus expectedStatus, CancellationToken cancellationToken);
    Task<bool> SoftDeleteAsync(Guid competitionId, CompetitionStatus expectedStatus, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
}

public static class CompetitionManagementPolicy
{
    public static OperationResult<CompetitionManagementFailureCode> ValidateUpdate(CompetitionView current, UpdateCompetitionCommand command)
    {
        return OperationResult<CompetitionManagementFailureCode>.Success();
    }

    public static OperationResult<CompetitionManagementFailureCode> ValidateDelete(CompetitionStatus status) => status switch
    {
        CompetitionStatus.Running or CompetitionStatus.Paused =>
            OperationResult<CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionActive,
                "An active competition cannot be deleted."),
        CompetitionStatus.Finished =>
            OperationResult<CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionFinished,
                "Finished competitions are read-only."),
        _ => OperationResult<CompetitionManagementFailureCode>.Success()
    };
}

public sealed class CreateCompetition(ICompetitionManagementStore store)
{
    public Task<CompetitionCreationResult> ExecuteAsync(
        CreateCompetitionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 160)
        {
            return Task.FromResult(new CompetitionCreationResult(
                CompetitionCreationState.InvalidRequest,
                Detail: "Competition title is required and must be at most 160 characters."));
        }
        if (command.MaxTeamMembers < 1)
        {
            return Task.FromResult(new CompetitionCreationResult(
                CompetitionCreationState.InvalidRequest,
                Detail: "MaxTeamMembers must be greater than zero."));
        }
        var schedule = CompetitionLifecyclePolicy.ValidateSchedule(command.StartTime, command.EndTime);
        if (!schedule.Succeeded)
        {
            return Task.FromResult(new CompetitionCreationResult(
                CompetitionCreationState.InvalidRequest,
                Detail: schedule.ErrorMessage));
        }
        return store.CreateAsync(command, cancellationToken);
    }
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
    public async Task<OperationResult<CompetitionView, CompetitionManagementFailureCode>> ExecuteAsync(UpdateCompetitionCommand command, CancellationToken ct = default)
    {
        var current = await store.FindAsync(command.CompetitionId, true, ct);
        if (current is null)
            return OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionNotFound,
                "Competition was not found.");
        var mutation = CompetitionManagementPolicy.ValidateUpdate(current, command);
        if (!mutation.Succeeded)
            return OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                mutation.FailureCode!.Value, mutation.ErrorMessage!);
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 160)
            return OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.InvalidTitle,
                "Competition title is required and must be at most 160 characters.");
        if (command.MaxTeamMembers < 1)
            return OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.InvalidTeamSize,
                "MaxTeamMembers must be greater than zero.");
        var schedule = CompetitionLifecyclePolicy.ValidateSchedule(command.StartTime, command.EndTime);
        if (!schedule.Succeeded)
            return OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                schedule.FailureCode == CompetitionLifecyclePolicy.FailureCode.InvalidSchedule
                    ? CompetitionManagementFailureCode.InvalidSchedule
                    : CompetitionManagementFailureCode.CompetitionConflict,
                schedule.ErrorMessage!);
        var updated = await store.UpdateAsync(command, current.Status, ct);
        return updated is null
            ? OperationResult<CompetitionView, CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionConflict,
                "Competition status changed concurrently.")
            : OperationResult<CompetitionView, CompetitionManagementFailureCode>.Success(updated);
    }
}

public sealed class DeleteCompetition(ICompetitionManagementStore store)
{
    public async Task<OperationResult<CompetitionManagementFailureCode>> ExecuteAsync(Guid competitionId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await store.FindAsync(competitionId, true, ct);
        if (current is null)
            return OperationResult<CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionNotFound,
                "Competition was not found.");
        var mutation = CompetitionManagementPolicy.ValidateDelete(current.Status);
        if (!mutation.Succeeded)
            return mutation;
        return await store.SoftDeleteAsync(competitionId, current.Status, actorId, now, ct)
            ? OperationResult<CompetitionManagementFailureCode>.Success()
            : OperationResult<CompetitionManagementFailureCode>.Failure(
                CompetitionManagementFailureCode.CompetitionConflict,
                "Competition changed concurrently.");
    }
}
