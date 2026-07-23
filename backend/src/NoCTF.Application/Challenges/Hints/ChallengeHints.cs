using NoCTF.Application.Common;

namespace NoCTF.Application.Challenges.Hints;

public sealed record ChallengeHintView(
    Guid Id,
    Guid CompetitionChallengeId,
    string Content,
    long Cost,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveChallengeHintCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? HintId,
    string Content,
    long Cost,
    DateTimeOffset? PublishedAt,
    DateTimeOffset Now);

public sealed record HintUnlockResult(ChallengeHintView Hint, bool Created);

public enum HintUnlockFailure
{
    NotFound,
    InsufficientScore
}

public sealed record HintUnlockAttempt(
    HintUnlockResult? Result,
    HintUnlockFailure? Failure)
{
    public static HintUnlockAttempt Success(HintUnlockResult result) => new(result, null);
    public static HintUnlockAttempt Failed(HintUnlockFailure failure) => new(null, failure);
}

public interface IChallengeHintStore
{
    Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken);
    Task<ChallengeHintView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        CancellationToken cancellationToken);
    Task<ChallengeHintView?> SaveAsync(
        SaveChallengeHintCommand command,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<HintUnlockAttempt> UnlockAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageChallengeHints(IChallengeHintStore store)
{
    public Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken ct = default) =>
        store.ListAsync(competitionId, competitionChallengeId, ct);

    public Task<ChallengeHintView?> GetAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        CancellationToken ct = default) =>
        store.FindAsync(competitionId, competitionChallengeId, hintId, ct);

    public async Task<OperationResult<ChallengeHintView>> SaveAsync(
        SaveChallengeHintCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
            return OperationResult<ChallengeHintView>.Failure("invalid_hint", "Hint content is required.");
        if (command.Cost < 0)
            return OperationResult<ChallengeHintView>.Failure("invalid_hint_cost", "Hint cost cannot be negative.");
        var result = await store.SaveAsync(command with { Content = command.Content.Trim() }, ct);
        return result is null
            ? OperationResult<ChallengeHintView>.Failure("hint_not_found", "Competition challenge or hint was not found.")
            : OperationResult<ChallengeHintView>.Success(result);
    }

    public async Task<OperationResult> DeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        await store.DeleteAsync(competitionId, competitionChallengeId, hintId, now, ct)
            ? OperationResult.Success()
            : OperationResult.Failure("hint_not_found", "Hint was not found.");
}

public sealed class UnlockChallengeHint(IChallengeHintStore store)
{
    public async Task<OperationResult<HintUnlockResult>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var result = await store.UnlockAsync(
            competitionId, competitionChallengeId, hintId, userId, now, ct);
        return result.Failure switch
        {
            HintUnlockFailure.NotFound =>
                OperationResult<HintUnlockResult>.Failure(
                    "hint_not_found",
                    "The hint is unavailable."),
            HintUnlockFailure.InsufficientScore =>
                OperationResult<HintUnlockResult>.Failure(
                    "insufficient_score",
                    "The team has insufficient authoritative score."),
            _ => OperationResult<HintUnlockResult>.Success(result.Result!)
        };
    }
}
