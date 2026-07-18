using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Ports;

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
}

/// <summary>Applies a relational ban ruling and schedules retroactive score reconstruction.</summary>
public sealed class ModerateTeam(ITeamModerationStore store, IScoringRebuildQueue rebuildQueue)
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

        await rebuildQueue.EnqueueAsync(
            command.CompetitionId,
            command.Ban ? ScoringRebuildReason.TeamBanned : ScoringRebuildReason.TeamUnbanned,
            cancellationToken);
        return OperationResult.Success();
    }
}
