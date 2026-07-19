using NoCTF.Application.Common;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Teams.Moderation;

public sealed record TeamModerationCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ActorId,
    bool Ban,
    string? Reason,
    DateTimeOffset OccurredAt);

public interface ITeamModerationStore
{
    Task<OperationResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken);
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
public sealed class ModerateTeam(ITeamModerationStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(
        TeamModerationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Ban && string.IsNullOrWhiteSpace(command.Reason))
            return OperationResult.Failure("ban_reason_required", "A ban reason is required.");

        var result = await store.ApplyAsync(command, cancellationToken);
        if (!result.Succeeded)
            return result;

        await cache.InvalidateAsync(command.CompetitionId, cancellationToken);
        await scheduler.EnqueueCompetitionRebuildAsync(command.CompetitionId, cancellationToken);
        return OperationResult.Success();
    }
}
