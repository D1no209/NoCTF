using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Visibility;

public sealed record CompetitionVisibilityAccessDecision(
    GameMode GameMode,
    CompetitionStatus CompetitionStatus,
    CompetitionLeaderboardVisibility Visibility,
    LeaderboardDataScope DataScope,
    int VisibilityRevision);

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
    CompetitionLeaderboardVisibility ConfiguredVisibility,
    CompetitionLeaderboardVisibility EffectiveVisibility,
    DateTimeOffset? StartsAt,
    DateTimeOffset? AppliedAt,
    int Revision);

public enum CompetitionVisibilityMutationState
{
    Updated,
    NotFound,
    RevisionConflict,
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
    CompetitionLeaderboardVisibility Visibility,
    DateTimeOffset? StartsAt,
    int ExpectedRevision,
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
            && command.Visibility != CompetitionLeaderboardVisibility.Normal)
            return CompetitionVisibilityMutationState.CompetitionFinished;
        if (command.Visibility == CompetitionLeaderboardVisibility.Normal)
            return command.StartsAt is null
                ? null
                : CompetitionVisibilityMutationState.InvalidSchedule;
        if (command.StartsAt is not { } startsAt)
            return null;
        return startsAt <= command.Now
               || startsAt < competitionStartTime
               || startsAt >= competitionEndTime
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

    Task ApplyScheduledAsync(
        Guid competitionId,
        int expectedRevision,
        DateTimeOffset now,
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
        if (current.Revision != command.ExpectedRevision)
            return new(CompetitionVisibilityMutationState.RevisionConflict, current);
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

public sealed class ApplyScheduledCompetitionVisibility(ICompetitionVisibilityStore store)
{
    public Task ExecuteAsync(
        Guid competitionId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        store.ApplyScheduledAsync(
            competitionId,
            expectedRevision,
            now,
            cancellationToken);
}
