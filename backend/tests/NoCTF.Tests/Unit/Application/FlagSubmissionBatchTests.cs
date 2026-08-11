using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class FlagSubmissionBatchTests
{
    [Test]
    public async Task Invalid_flag_rejects_entire_batch_before_store_write()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["flag{valid}", "\0"],
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.FlagInvalid);
        await Assert.That(store.BatchWrites).IsEqualTo(0);
    }

    [Test]
    public async Task Awd_batch_is_persisted_with_one_atomic_store_call_in_input_order()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["flag{one}", "flag{two}"],
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.BatchWrites).IsEqualTo(1);
        await Assert.That(store.LastFlags).IsEquivalentTo(["flag{one}", "flag{two}"]);
    }

    [Test]
    public async Task Batch_over_item_limit_is_rejected_before_store_write()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Repeat("flag{valid}", SubmitFlag.MaximumBatchFlagCount + 1).ToArray(),
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.FlagBatchLimitExceeded);
        await Assert.That(store.BatchWrites).IsEqualTo(0);
    }

    [Test]
    public async Task Batch_over_total_byte_limit_is_rejected_before_individual_validation()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());
        var oversizedItem = new string('a', 4097);

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Repeat(oversizedItem, SubmitFlag.MaximumBatchFlagCount).ToArray(),
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.FlagBatchLimitExceeded);
        await Assert.That(store.BatchWrites).IsEqualTo(0);
    }

    [Test]
    public async Task Batch_at_both_limits_is_accepted_atomically()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());
        var maximumItem = new string('a', 4096);

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Repeat(maximumItem, SubmitFlag.MaximumBatchFlagCount).ToArray(),
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.BatchWrites).IsEqualTo(1);
        await Assert.That(store.LastFlags.Count).IsEqualTo(SubmitFlag.MaximumBatchFlagCount);
    }

    private sealed class Store : IGameplayFactIntakeStore
    {
        public int BatchWrites { get; private set; }
        public IReadOnlyList<string> LastFlags { get; private set; } = [];

        public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameplayFactAdmissionSnapshot?>(new(
                competitionId,
                Guid.NewGuid(),
                competitionChallengeId,
                GameMode.Awd,
                1,
                1,
                "{}",
                "{}",
                0,
                0,
                CompetitionStatus.Running,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(1),
                false,
                false,
                true,
                false,
                false,
                true,
                true));

        public Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
            FlagGameplayFactReceived received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The batch path must be used.");

        public Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
            IReadOnlyList<FlagGameplayFactReceived> received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken)
        {
            BatchWrites++;
            LastFlags = received.Select(item => item.Value).ToArray();
            return Task.FromResult<IReadOnlyList<GameplayFactAcceptanceResult>>(
                received.Select(item => new GameplayFactAcceptanceResult(
                    GameplayFactAcceptanceState.Created,
                    item.GameplayFactId,
                    item.OccurredAt)).ToArray());
        }

        public Task<GameplayFactAcceptanceResult> TryAcceptFixAsync(
            FixGameplayFactReceived received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class Policy : IGameplayFactAdmissionModePolicy
    {
        public GameplayFactAdmissionRules GetRules(
            GameMode mode,
            string competitionConfigurationJson,
            string challengeConfigurationJson) =>
            new(true, false, null, null);
    }
}
