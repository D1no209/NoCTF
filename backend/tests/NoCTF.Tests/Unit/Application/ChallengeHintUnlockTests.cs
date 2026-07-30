using NoCTF.Application.Challenges.Hints;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeHintUnlockTests
{
    [Test]
    [Arguments(HintUnlockFailure.NotFound, "hint_not_found")]
    [Arguments(HintUnlockFailure.InsufficientScore, "insufficient_score")]
    public async Task Unlock_maps_store_failures_to_stable_codes(
        HintUnlockFailure failure,
        string expectedCode)
    {
        var result = await new UnlockChallengeHint(new Store(
                HintUnlockAttempt.Failed(failure)))
            .ExecuteAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
    }

    [Test]
    public async Task Unlock_preserves_created_fact_state()
    {
        var hint = new ChallengeHintView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "First clue",
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
        var result = await new UnlockChallengeHint(new Store(
                HintUnlockAttempt.Success(new HintUnlockResult(hint, true))))
            .ExecuteAsync(
                Guid.NewGuid(),
                hint.CompetitionChallengeId,
                hint.Id,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Created).IsTrue();
    }

    private sealed class Store(HintUnlockAttempt attempt) : IChallengeHintStore
    {
        public Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            CancellationToken cancellationToken,
            bool includeDeleted = false) =>
            throw new NotSupportedException();

        public Task<ChallengeHintView?> FindAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            CancellationToken cancellationToken,
            bool includeDeleted = false) =>
            throw new NotSupportedException();

        public Task<ChallengeHintView?> SaveAsync(
            SaveChallengeHintCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RestoreAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HintUnlockAttempt> UnlockAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(attempt);
    }
}
