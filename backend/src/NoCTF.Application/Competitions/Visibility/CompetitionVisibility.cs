using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Visibility;

public sealed record CompetitionVisibilityAccessDecision(
    GameMode GameMode,
    CompetitionStatus CompetitionStatus,
    CompetitionLeaderboardVisibility Visibility,
    LeaderboardDataScope DataScope);

public interface ICompetitionVisibilityAccess
{
    Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record CompetitionVisibilityConfigurationView(
    Guid CompetitionId,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset CompetitionStartTime,
    DateTimeOffset CompetitionEndTime,
    CompetitionLeaderboardVisibility EffectiveVisibility,
    DateTimeOffset? FrozenStartAt,
    DateTimeOffset? HiddenStartAt);

public enum CompetitionVisibilityMutationState
{
    Updated,
    NotFound,
    InvalidSchedule,
    CompetitionFinished
}

public sealed record CompetitionVisibilityMutationResult(
    CompetitionVisibilityMutationState State,
    CompetitionVisibilityConfigurationView? Configuration = null)
{
    public bool Succeeded => State == CompetitionVisibilityMutationState.Updated;
}

public sealed record UpdateCompetitionVisibilityCommand(
    Guid CompetitionId,
    DateTimeOffset? FrozenStartAt,
    DateTimeOffset? HiddenStartAt,
    Guid ActorId,
    string? Reason,
    DateTimeOffset Now);

public static class CompetitionVisibilityRules
{
    public static CompetitionVisibilityMutationState? Validate(
        CompetitionStatus status,
        DateTimeOffset competitionStartTime,
        DateTimeOffset competitionEndTime,
        UpdateCompetitionVisibilityCommand command)
    {
        if (status == CompetitionStatus.Finished
            && (command.FrozenStartAt is not null || command.HiddenStartAt is not null))
            return CompetitionVisibilityMutationState.CompetitionFinished;

        return new[] { command.FrozenStartAt, command.HiddenStartAt }
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Any(startsAt => startsAt < competitionStartTime || startsAt >= competitionEndTime)
                ? CompetitionVisibilityMutationState.InvalidSchedule
                : null;
    }
}

public interface ICompetitionVisibilityStore
{
    Task<CompetitionVisibilityConfigurationView?> GetAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CompetitionVisibilityMutationResult> UpdateAsync(
        UpdateCompetitionVisibilityCommand command,
        CancellationToken cancellationToken);

}

public sealed class GetCompetitionVisibility(ICompetitionVisibilityStore store)
{
    public Task<CompetitionVisibilityConfigurationView?> ExecuteAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        store.GetAsync(competitionId, now, cancellationToken);
}

public sealed class UpdateCompetitionVisibility(ICompetitionVisibilityStore store)
{
    public async Task<CompetitionVisibilityMutationResult> ExecuteAsync(
        UpdateCompetitionVisibilityCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await store.GetAsync(
            command.CompetitionId,
            command.Now,
            cancellationToken);
        if (current is null)
            return new(CompetitionVisibilityMutationState.NotFound);
        var validationFailure = CompetitionVisibilityRules.Validate(
            current.CompetitionStatus,
            current.CompetitionStartTime,
            current.CompetitionEndTime,
            command);
        if (validationFailure is { } state)
            return new(state, current);

        return await store.UpdateAsync(command, cancellationToken);
    }
}
