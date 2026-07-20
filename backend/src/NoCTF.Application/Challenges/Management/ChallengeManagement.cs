using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Management;

public sealed record CreateChallengeCommand(Guid CompetitionId, string Title, string? Description, string Direction, int Order, DateTimeOffset CreatedAt);
public sealed record UpdateChallengeCommand(Guid CompetitionId, Guid ChallengeId, string Title, string? Description, string Direction, int Order, DateTimeOffset UpdatedAt);
public sealed record ChallengeView(Guid Id, Guid CompetitionId, string Title, string? Description, string Direction, int Order, bool IsPublished, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public enum ChallengeMutationFailure
{
    CompetitionNotFound,
    ChallengeLocked,
    ChallengeNotFound,
    ChallengeOrderConflict
}
public sealed record ChallengeMutationResult(ChallengeView? Challenge, ChallengeMutationFailure? Failure = null);
public sealed record ChallengeCompetitionContext(GameMode Mode, CompetitionStatus Status);

public interface IChallengeManagementStore
{
    Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> CreateAsync(
        CreateChallengeCommand command,
        string configurationJson,
        CancellationToken cancellationToken);
    Task<ChallengeView?> FindAsync(Guid competitionId, Guid challengeId, bool includeUnpublished, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeView>> ListAsync(Guid competitionId, bool includeUnpublished, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> UpdateAsync(UpdateChallengeCommand command, CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> SetPublishedAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ChallengeMutationFailure?> SoftDeleteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);
}

internal static class ChallengeMutationFailureProtocol
{
    public static string Code(ChallengeMutationFailure failure) => failure switch
    {
        ChallengeMutationFailure.CompetitionNotFound => "competition_not_found",
        ChallengeMutationFailure.ChallengeLocked => "challenge_locked",
        ChallengeMutationFailure.ChallengeNotFound => "challenge_not_found",
        ChallengeMutationFailure.ChallengeOrderConflict => "challenge_order_conflict",
        _ => "challenge_conflict"
    };
}

public sealed class CreateChallenge(IChallengeManagementStore store, IChallengeConfigurationCatalog configurationCatalog)
{
    public async Task<OperationResult<ChallengeView>> ExecuteAsync(CreateChallengeCommand command, CancellationToken ct = default)
    {
        var error = Validate(command.Title, command.Direction, command.Order);
        if (error is not null) return OperationResult<ChallengeView>.Failure(error.Value.Code, error.Value.Message);
        var competition = await store.GetCompetitionAsync(command.CompetitionId, ct);
        if (competition is null) return OperationResult<ChallengeView>.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(competition.Status))
            return OperationResult<ChallengeView>.Failure("challenge_locked", "Challenge definitions are locked for this competition.");
        var configurationJson = configurationCatalog.GetDefaultJson(competition.Mode);
        var result = await store.CreateAsync(
            command with { Title = command.Title.Trim(), Direction = command.Direction.Trim() },
            configurationJson,
            ct);
        return result.Challenge is not null ? OperationResult<ChallengeView>.Success(result.Challenge)
            : OperationResult<ChallengeView>.Failure(ChallengeMutationFailureProtocol.Code(result.Failure ?? ChallengeMutationFailure.ChallengeOrderConflict), "Challenge was not created.");
    }

    internal static (string Code, string Message)? Validate(string title, string direction, int order)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160) return ("invalid_title", "Challenge title is required and must be at most 160 characters.");
        if (string.IsNullOrWhiteSpace(direction) || direction.Length > 96) return ("invalid_direction", "Challenge direction is required and must be at most 96 characters.");
        if (order < 0) return ("invalid_order", "Challenge order cannot be negative.");
        return null;
    }
}

public sealed class GetChallenge(IChallengeManagementStore store)
{
    public Task<ChallengeView?> ExecuteAsync(Guid competitionId, Guid challengeId, bool includeUnpublished, CancellationToken ct = default) => store.FindAsync(competitionId, challengeId, includeUnpublished, ct);
}
public sealed class ListChallenges(IChallengeManagementStore store)
{
    public Task<IReadOnlyList<ChallengeView>> ExecuteAsync(Guid competitionId, bool includeUnpublished, CancellationToken ct = default) => store.ListAsync(competitionId, includeUnpublished, ct);
}

public sealed class UpdateChallenge(IChallengeManagementStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult<ChallengeView>> ExecuteAsync(UpdateChallengeCommand command, CancellationToken ct = default)
    {
        var error = CreateChallenge.Validate(command.Title, command.Direction, command.Order);
        if (error is not null) return OperationResult<ChallengeView>.Failure(error.Value.Code, error.Value.Message);

        var competition = await store.GetCompetitionAsync(command.CompetitionId, ct);
        if (competition is null)
            return OperationResult<ChallengeView>.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(competition.Status))
            return OperationResult<ChallengeView>.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var result = await store.UpdateAsync(command with { Title = command.Title.Trim(), Direction = command.Direction.Trim() }, ct);
        if (result.Challenge is null) return OperationResult<ChallengeView>.Failure(ChallengeMutationFailureProtocol.Code(result.Failure ?? ChallengeMutationFailure.ChallengeOrderConflict), "Challenge was not updated.");
        await cache.InvalidateAsync(command.CompetitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(command.CompetitionId, ct);
        return OperationResult<ChallengeView>.Success(result.Challenge);
    }
}

public sealed class SetChallengePublished(IChallengeManagementStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken ct = default)
    {
        var competition = await store.GetCompetitionAsync(competitionId, ct);
        if (competition is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(competition.Status))
            return OperationResult.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var failure = await store.SetPublishedAsync(competitionId, challengeId, published, now, ct);
        if (failure is not null) return OperationResult.Failure(ChallengeMutationFailureProtocol.Code(failure.Value), "Challenge publication was rejected.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult.Success();
    }
}

public sealed class DeleteChallenge(IChallengeManagementStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var competition = await store.GetCompetitionAsync(competitionId, ct);
        if (competition is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(competition.Status))
            return OperationResult.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var failure = await store.SoftDeleteAsync(competitionId, challengeId, actorId, now, ct);
        if (failure is not null) return OperationResult.Failure(ChallengeMutationFailureProtocol.Code(failure.Value), "Challenge was not deleted.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult.Success();
    }
}

public static class ChallengeMutationPolicy
{
    public static bool IsLocked(CompetitionStatus status) =>
        status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished;
}
