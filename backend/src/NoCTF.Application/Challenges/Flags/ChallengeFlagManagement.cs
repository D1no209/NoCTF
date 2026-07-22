using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Flags;

public sealed record CreateChallengeFlagCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    DateTimeOffset CreatedAt,
    Guid? StageId = null,
    Guid? ChallengeInstanceId = null)
{
    public override string ToString() =>
        $"{nameof(CreateChallengeFlagCommand)} {{ CompetitionId = {CompetitionId}, CompetitionChallengeId = {CompetitionChallengeId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record UpdateChallengeFlagCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid FlagId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    long ExpectedRowVersion,
    DateTimeOffset UpdatedAt,
    Guid? StageId = null,
    Guid? ChallengeInstanceId = null)
{
    public override string ToString() =>
        $"{nameof(UpdateChallengeFlagCommand)} {{ CompetitionId = {CompetitionId}, CompetitionChallengeId = {CompetitionChallengeId}, FlagId = {FlagId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record ChallengeFlagView(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    string Flag,
    DateTimeOffset? ValidStart,
    DateTimeOffset? ValidEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long RowVersion,
    Guid? StageId = null,
    Guid? ChallengeInstanceId = null)
{
    public override string ToString() =>
        $"{nameof(ChallengeFlagView)} {{ Id = {Id}, CompetitionId = {CompetitionId}, CompetitionChallengeId = {CompetitionChallengeId}, TeamId = {TeamId}, Flag = [REDACTED] }}";
}

public sealed record ChallengeFlagScope(
    CompetitionStatus CompetitionStatus,
    bool TeamExists,
    GameMode Mode = GameMode.Ctf,
    bool StageExists = true,
    bool ChallengeInstanceExists = true);
public enum ChallengeFlagMutationFailure
{
    CompetitionNotFound,
    ChallengeNotFound,
    FlagLocked,
    TeamNotFound,
    FlagWindowConflict,
    FlagNotFound,
    FlagConflict,
    InvalidScope
}
public sealed record ChallengeFlagMutationResult(ChallengeFlagView? Flag, ChallengeFlagMutationFailure? Failure = null);

public interface IChallengeFlagStore
{
    Task<ChallengeFlagScope?> LoadScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? teamId,
        Guid? stageId,
        Guid? challengeInstanceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChallengeFlagView>> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken);

    Task<ChallengeFlagView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid flagId,
        CancellationToken cancellationToken);

    Task<ChallengeFlagMutationResult> CreateAsync(
        CreateChallengeFlagCommand command,
        CancellationToken cancellationToken);

    Task<ChallengeFlagMutationResult> UpdateAsync(
        UpdateChallengeFlagCommand command,
        CancellationToken cancellationToken);

    Task<ChallengeFlagMutationFailure?> DeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid flagId,
        long expectedRowVersion,
        CancellationToken cancellationToken);
}

internal static class ChallengeFlagMutationFailureProtocol
{
    public static string Code(ChallengeFlagMutationFailure failure) => failure switch
    {
        ChallengeFlagMutationFailure.CompetitionNotFound => "competition_not_found",
        ChallengeFlagMutationFailure.ChallengeNotFound => "challenge_not_found",
        ChallengeFlagMutationFailure.FlagLocked => "flag_locked",
        ChallengeFlagMutationFailure.TeamNotFound => "team_not_found",
        ChallengeFlagMutationFailure.FlagWindowConflict => "flag_window_conflict",
        ChallengeFlagMutationFailure.FlagNotFound => "flag_not_found",
        ChallengeFlagMutationFailure.FlagConflict => "flag_conflict",
        ChallengeFlagMutationFailure.InvalidScope => "invalid_flag_scope",
        _ => "flag_conflict"
    };
}

public sealed class ListChallengeFlags(IChallengeFlagStore store)
{
    public Task<IReadOnlyList<ChallengeFlagView>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken ct = default) =>
        store.ListAsync(competitionId, competitionChallengeId, ct);
}

public sealed class GetChallengeFlag(IChallengeFlagStore store)
{
    public Task<ChallengeFlagView?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid flagId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, competitionChallengeId, flagId, ct);
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

        var scope = await store.LoadScopeAsync(command.CompetitionId, command.CompetitionChallengeId, command.TeamId,
            command.StageId, command.ChallengeInstanceId, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(
            scope, command.TeamId, command.StageId, command.ChallengeInstanceId);
        if (scopeError is not null)
            return OperationResult<ChallengeFlagView>.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var result = await store.CreateAsync(command, ct);
        if (result.Flag is null)
            return OperationResult<ChallengeFlagView>.Failure(
                ChallengeFlagMutationFailureProtocol.Code(result.Failure ?? ChallengeFlagMutationFailure.FlagConflict),
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

        var scope = await store.LoadScopeAsync(command.CompetitionId, command.CompetitionChallengeId, command.TeamId,
            command.StageId, command.ChallengeInstanceId, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(
            scope, command.TeamId, command.StageId, command.ChallengeInstanceId);
        if (scopeError is not null)
            return OperationResult<ChallengeFlagView>.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var result = await store.UpdateAsync(command, ct);
        if (result.Flag is null)
            return OperationResult<ChallengeFlagView>.Failure(
                ChallengeFlagMutationFailureProtocol.Code(result.Failure ?? ChallengeFlagMutationFailure.FlagConflict),
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
        Guid competitionChallengeId,
        Guid flagId,
        long expectedRowVersion,
        CancellationToken ct = default)
    {
        if (expectedRowVersion < 0)
            return OperationResult.Failure("invalid_row_version", "Expected RowVersion cannot be negative.");

        var scope = await store.LoadScopeAsync(
            competitionId, competitionChallengeId, teamId: null, stageId: null, challengeInstanceId: null, ct);
        var scopeError = ChallengeFlagPolicy.ValidateScope(
            scope, teamId: null, stageId: null, challengeInstanceId: null, validateDimensions: false);
        if (scopeError is not null)
            return OperationResult.Failure(scopeError.Value.Code, scopeError.Value.Message);

        var failure = await store.DeleteAsync(competitionId, competitionChallengeId, flagId, expectedRowVersion, ct);
        if (failure is not null)
            return OperationResult.Failure(ChallengeFlagMutationFailureProtocol.Code(failure.Value), "Challenge Flag was not deleted.");

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

    public static (string Code, string Message)? ValidateScope(
        ChallengeFlagScope? scope,
        Guid? teamId,
        Guid? stageId,
        Guid? challengeInstanceId,
        bool validateDimensions = true)
    {
        if (scope is null)
            return ("challenge_not_found", "Challenge was not found.");
        if (scope.CompetitionStatus == CompetitionStatus.Finished)
            return ("flag_locked", "Finished competition Flags are read-only.");
        if (teamId is not null && !scope.TeamExists)
            return ("team_not_found", "The Team-specific Flag scope was not found.");
        if (!validateDimensions) return null;
        if (scope.Mode == GameMode.Penetration
            && (teamId is null || stageId is null))
            return ("penetration_flag_scope_required",
                "Penetration Flags require Team and Stage scope; Challenge instance scope is optional.");
        if (scope.Mode != GameMode.Penetration && (stageId is not null || challengeInstanceId is not null))
            return ("penetration_flag_scope_not_allowed",
                "Stage and Challenge instance scope are allowed only for Penetration Flags.");
        if (!scope.StageExists)
            return ("stage_not_found", "The Penetration Stage was not found.");
        if (challengeInstanceId is not null && !scope.ChallengeInstanceExists)
            return ("challenge_instance_not_found", "The Penetration Challenge instance was not found.");
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
