using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Scoring;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeHintManagementTests
{
    [Test]
    public async Task Save_accepts_the_configured_maximum_hint_cost()
    {
        var store = new Store();

        var result = await new ManageChallengeHints(store).SaveAsync(
            Command(ScoreValueLimits.MaximumConfiguredValue));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.SaveCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(ScoreValueLimits.MaximumConfiguredValue + 1)]
    public async Task Save_rejects_hint_cost_outside_the_configured_range(long cost)
    {
        var store = new Store();

        var result = await new ManageChallengeHints(store).SaveAsync(Command(cost));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeHintFailureCode.InvalidHintCost);
        await Assert.That(store.SaveCalls).IsEqualTo(0);
    }

    private static SaveChallengeHintCommand Command(long cost) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        IsCreate: true,
        Content: "A useful hint",
        Cost: cost,
        PublishedAt: null,
        Now: DateTimeOffset.UtcNow);

    private sealed class Store : IChallengeHintStore
    {
        public int SaveCalls { get; private set; }

        public Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            bool includeDeleted,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChallengeHintView?> FindAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            bool includeDeleted,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChallengeHintSaveResult> SaveAsync(
            SaveChallengeHintCommand command,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(new ChallengeHintSaveResult(new(
                command.HintId!.Value,
                command.CompetitionChallengeId,
                command.Content,
                command.Cost,
                command.PublishedAt,
                null,
                command.Now,
                command.Now)));
        }

        public Task<bool> DeleteAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RestoreAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<HintUnlockAttempt> UnlockAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid hintId,
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
