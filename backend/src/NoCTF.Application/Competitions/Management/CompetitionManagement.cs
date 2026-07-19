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

public interface ICompetitionManagementStore
{
    Task<CompetitionView> CreateAsync(CreateCompetitionCommand command, CancellationToken cancellationToken);
    Task<CompetitionView?> FindAsync(Guid competitionId, bool includeDraft, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionView>> ListAsync(bool includeDraft, CancellationToken cancellationToken);
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
