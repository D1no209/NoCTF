using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Challenges.Management;

public sealed record CreateChallengeCommand(Guid CompetitionId, string Title, string? Description, string Direction, int Order, DateTimeOffset CreatedAt);
public sealed record UpdateChallengeCommand(Guid CompetitionId, Guid ChallengeId, string Title, string? Description, string Direction, int Order, DateTimeOffset UpdatedAt);
public sealed record ChallengeView(Guid Id, Guid CompetitionId, string Title, string? Description, string Direction, int Order, bool IsPublished, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ChallengeMutationResult(ChallengeView? Challenge, string? ErrorCode);

public interface IChallengeManagementStore
{
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> CreateAsync(CreateChallengeCommand command, CancellationToken cancellationToken);
    Task<ChallengeView?> FindAsync(Guid competitionId, Guid challengeId, bool includeUnpublished, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChallengeView>> ListAsync(Guid competitionId, bool includeUnpublished, CancellationToken cancellationToken);
    Task<ChallengeMutationResult> UpdateAsync(UpdateChallengeCommand command, CancellationToken cancellationToken);
    Task<string?> SetPublishedAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken cancellationToken);
    Task<string?> SoftDeleteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class CreateChallenge(IChallengeManagementStore store)
{
    public async Task<OperationResult<ChallengeView>> ExecuteAsync(CreateChallengeCommand command, CancellationToken ct = default)
    {
        var error = Validate(command.Title, command.Direction, command.Order);
        if (error is not null) return OperationResult<ChallengeView>.Failure(error.Value.Code, error.Value.Message);
        var status = await store.GetCompetitionStatusAsync(command.CompetitionId, ct);
        if (status is null) return OperationResult<ChallengeView>.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(status.Value))
            return OperationResult<ChallengeView>.Failure("challenge_locked", "Challenge definitions are locked for this competition.");
        var result = await store.CreateAsync(command with { Title = command.Title.Trim(), Direction = command.Direction.Trim() }, ct);
        return result.Challenge is not null ? OperationResult<ChallengeView>.Success(result.Challenge)
            : OperationResult<ChallengeView>.Failure(result.ErrorCode ?? "challenge_conflict", "Challenge was not created.");
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

        var status = await store.GetCompetitionStatusAsync(command.CompetitionId, ct);
        if (status is null)
            return OperationResult<ChallengeView>.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(status.Value))
            return OperationResult<ChallengeView>.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var result = await store.UpdateAsync(command with { Title = command.Title.Trim(), Direction = command.Direction.Trim() }, ct);
        if (result.Challenge is null) return OperationResult<ChallengeView>.Failure(result.ErrorCode ?? "challenge_conflict", "Challenge was not updated.");
        await cache.InvalidateAsync(command.CompetitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(command.CompetitionId, ct);
        return OperationResult<ChallengeView>.Success(result.Challenge);
    }
}

public sealed class SetChallengePublished(IChallengeManagementStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid challengeId, bool published, DateTimeOffset now, CancellationToken ct = default)
    {
        var status = await store.GetCompetitionStatusAsync(competitionId, ct);
        if (status is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(status.Value))
            return OperationResult.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var error = await store.SetPublishedAsync(competitionId, challengeId, published, now, ct);
        if (error is not null) return OperationResult.Failure(error, "Challenge publication was rejected.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult.Success();
    }
}

public sealed class DeleteChallenge(IChallengeManagementStore store, ILeaderboardCache cache, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var status = await store.GetCompetitionStatusAsync(competitionId, ct);
        if (status is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (ChallengeMutationPolicy.IsLocked(status.Value))
            return OperationResult.Failure("challenge_locked", "Challenge definitions are locked for this competition.");

        var error = await store.SoftDeleteAsync(competitionId, challengeId, actorId, now, ct);
        if (error is not null) return OperationResult.Failure(error, "Challenge was not deleted.");
        await cache.InvalidateAsync(competitionId, ct);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, ct);
        return OperationResult.Success();
    }
}

internal static class ChallengeMutationPolicy
{
    public static bool IsLocked(CompetitionStatus status) =>
        status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished;
}
