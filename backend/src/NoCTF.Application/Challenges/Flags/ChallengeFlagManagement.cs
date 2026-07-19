using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Flags;

public sealed record CreateChallengeFlagCommand(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    DateTimeOffset CreatedAt)
{
    public override string ToString() =>
        $"{nameof(CreateChallengeFlagCommand)} {{ CompetitionId = {CompetitionId}, ChallengeId = {ChallengeId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record UpdateChallengeFlagCommand(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid FlagId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    long ExpectedRowVersion,
    DateTimeOffset UpdatedAt)
{
    public override string ToString() =>
        $"{nameof(UpdateChallengeFlagCommand)} {{ CompetitionId = {CompetitionId}, ChallengeId = {ChallengeId}, FlagId = {FlagId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record ChallengeFlagView(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long RowVersion)
{
    public override string ToString() =>
        $"{nameof(ChallengeFlagView)} {{ Id = {Id}, CompetitionId = {CompetitionId}, ChallengeId = {ChallengeId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record ChallengeFlagScope(CompetitionStatus CompetitionStatus, bool TeamExists);
public sealed record ChallengeFlagMutationResult(ChallengeFlagView? Flag, string? ErrorCode);

public interface IChallengeFlagStore
{
    Task<ChallengeFlagScope?> LoadScopeAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChallengeFlagView>> ListAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken cancellationToken);

    Task<ChallengeFlagView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        CancellationToken cancellationToken);

    Task<ChallengeFlagMutationResult> CreateAsync(
        CreateChallengeFlagCommand command,
        CancellationToken cancellationToken);

    Task<ChallengeFlagMutationResult> UpdateAsync(
        UpdateChallengeFlagCommand command,
        CancellationToken cancellationToken);

    Task<string?> DeleteAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        long expectedRowVersion,
        CancellationToken cancellationToken);
}

public sealed class ListChallengeFlags(IChallengeFlagStore store)
{
    public Task<IReadOnlyList<ChallengeFlagView>> ExecuteAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct = default) =>
        store.ListAsync(competitionId, challengeId, ct);
}

public sealed class GetChallengeFlag(IChallengeFlagStore store)
{
    public Task<ChallengeFlagView?> ExecuteAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, challengeId, flagId, ct);
}

public sealed class CreateChallengeFlag(
    IChallengeFlagStore store,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<ChallengeFlagView>> ExecuteAsync(
        CreateChallengeFlagCommand command,
        CancellationToken ct = default)
    {
        var validation = ChallengeFlagPolicy.Validate(
            command.Flag,
            command.ValidStart,
            command.ValidEnd,
            expectedRowVersion: null);
        if (validation is not null)
            return OperationResult<ChallengeFlagView>.Failure(validation.Value.Code, validation.Value.Message);

        var scope = await store.LoadScopeAsync(command.CompetitionId, command.ChallengeId, command.TeamId, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(scope, command.TeamId);
        if (scopeError is not null)
            return OperationResult<ChallengeFlagView>.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var result = await store.CreateAsync(command, ct);
        if (result.Flag is null)
            return OperationResult<ChallengeFlagView>.Failure(
                result.ErrorCode ?? "flag_conflict",
                "Challenge Flag was not created.");

        await ChallengeFlagRebuild.ExecuteAsync(command.CompetitionId, cache, scheduler, ct);
        return OperationResult<ChallengeFlagView>.Success(result.Flag);
    }
}

public sealed class UpdateChallengeFlag(
    IChallengeFlagStore store,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<ChallengeFlagView>> ExecuteAsync(
        UpdateChallengeFlagCommand command,
        CancellationToken ct = default)
    {
        var validation = ChallengeFlagPolicy.Validate(
            command.Flag,
            command.ValidStart,
            command.ValidEnd,
            command.ExpectedRowVersion);
        if (validation is not null)
            return OperationResult<ChallengeFlagView>.Failure(validation.Value.Code, validation.Value.Message);

        var scope = await store.LoadScopeAsync(command.CompetitionId, command.ChallengeId, command.TeamId, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(scope, command.TeamId);
        if (scopeError is not null)
            return OperationResult<ChallengeFlagView>.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var result = await store.UpdateAsync(command, ct);
        if (result.Flag is null)
            return OperationResult<ChallengeFlagView>.Failure(
                result.ErrorCode ?? "flag_conflict",
                "Challenge Flag was not updated.");

        await ChallengeFlagRebuild.ExecuteAsync(command.CompetitionId, cache, scheduler, ct);
        return OperationResult<ChallengeFlagView>.Success(result.Flag);
    }
}

public sealed class DeleteChallengeFlag(
    IChallengeFlagStore store,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        long expectedRowVersion,
        CancellationToken ct = default)
    {
        if (expectedRowVersion < 0)
            return OperationResult.Failure("invalid_row_version", "Expected RowVersion cannot be negative.");

        var scope = await store.LoadScopeAsync(competitionId, challengeId, teamId: null, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(scope, teamId: null);
        if (scopeError is not null)
            return OperationResult.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var error = await store.DeleteAsync(competitionId, challengeId, flagId, expectedRowVersion, ct);
        if (error is not null)
            return OperationResult.Failure(error, "Challenge Flag was not deleted.");

        await ChallengeFlagRebuild.ExecuteAsync(competitionId, cache, scheduler, ct);
        return OperationResult.Success();
    }
}

internal static class ChallengeFlagPolicy
{
    public static (string Code, string Message)? Validate(
        string flag,
        DateTimeOffset? validStart,
        DateTimeOffset? validEnd,
        long? expectedRowVersion)
    {
        if (string.IsNullOrWhiteSpace(flag) || flag.Length > 4096)
            return ("invalid_flag", "Flag is required and cannot exceed 4096 characters.");
        if (validStart is not null && validEnd is not null && validStart >= validEnd)
            return ("invalid_flag_window", "ValidStart must be earlier than ValidEnd.");
        if (expectedRowVersion < 0)
            return ("invalid_row_version", "Expected RowVersion cannot be negative.");
        return null;
    }

    public static (string Code, string Message)? ValidateScope(ChallengeFlagScope? scope, Guid? teamId)
    {
        if (scope is null)
            return ("challenge_not_found", "Challenge was not found.");
        if (scope.CompetitionStatus == CompetitionStatus.Finished)
            return ("flag_locked", "Finished competition Flags are read-only.");
        if (teamId is not null && !scope.TeamExists)
            return ("team_not_found", "The Team-specific Flag scope was not found.");
        return null;
    }
}

file static class ChallengeFlagRebuild
{
    public static async Task ExecuteAsync(
        Guid competitionId,
        ILeaderboardCache cache,
        IBackgroundWorkScheduler scheduler,
        CancellationToken ct)
    {
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
    }
}
