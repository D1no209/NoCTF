using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Teams.Moderation;

public sealed record TeamModerationCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ActorId,
    bool Ban,
    string? Reason,
    DateTimeOffset OccurredAt);

public enum TeamModerationFailure
{
    CompetitionNotFound,
    CompetitionFinished,
    TeamNotFound
}

public sealed record TeamModerationStoreResult(TeamModerationFailure? Failure = null)
{
    public bool Succeeded => Failure is null;
}

public interface ITeamModerationStore
{
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<TeamModerationStoreResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken);
}

public interface ICompetitionModerationAuthorizer
{
    Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken);
    Task<bool> CanJudgeAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        CanModerateAsync(userId, competitionId, cancellationToken);
    Task<bool> CanObserveAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        CanJudgeAsync(userId, competitionId, cancellationToken);
}

/// <summary>Applies a relational ban ruling and schedules an in-process reconstruction.</summary>
public sealed class ModerateTeam(ITeamModerationStore store)
{
    public async Task<OperationResult> ExecuteAsync(
        TeamModerationCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = command.Reason?.Trim();
        if (command.Ban && (string.IsNullOrWhiteSpace(reason) || reason.Length > 512))
            return OperationResult.Failure("ban_reason_required", "A ban reason is required.");
        command = command with { Reason = reason };
        var status = await store.GetCompetitionStatusAsync(command.CompetitionId, cancellationToken);
        if (status is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (status == CompetitionStatus.Finished)
            return OperationResult.Failure("competition_finished", "Finished competitions are read-only.");

        var result = await store.ApplyAsync(command, cancellationToken);
        if (!result.Succeeded) return result.Failure switch
        {
            TeamModerationFailure.CompetitionNotFound => OperationResult.Failure("competition_not_found", "Competition was not found."),
            TeamModerationFailure.CompetitionFinished => OperationResult.Failure("competition_finished", "Finished competitions are read-only."),
            TeamModerationFailure.TeamNotFound => OperationResult.Failure("team_not_found", "Team was not found."),
            _ => OperationResult.Failure("team_moderation_failed", "Team moderation failed.")
        };

        return OperationResult.Success();
    }
}
