using System.Text;
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
    public async Task Flag_over_individual_byte_limit_rejects_entire_batch_before_store_write()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["flag{valid}", new string('a', 4097)],
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
    public async Task Awd_batch_has_no_item_or_total_byte_limit()
    {
        var store = new Store();
        var useCase = new SubmitFlag(store, new Policy());
        var maximumItem = new string('a', 4096);
        var flags = Enumerable.Repeat(maximumItem, 65).ToArray();

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            flags,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.BatchWrites).IsEqualTo(1);
        await Assert.That(store.LastFlags.Count).IsEqualTo(flags.Length);
        await Assert.That(store.LastFlags.Sum(Encoding.UTF8.GetByteCount)).IsGreaterThan(256 * 1024);
    }

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awdp)]
    public async Task Non_awd_modes_reject_flag_collections(GameMode mode)
    {
        var store = new Store(mode);
        var useCase = new SubmitFlag(store, new Policy());

        var result = await useCase.ExecuteBatchAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["flag{one}", "flag{two}"],
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.FlagBatchNotSupported);
        await Assert.That(store.BatchWrites).IsEqualTo(0);
    }

    private sealed class Store(GameMode mode = GameMode.Awd) : IGameplayFactIntakeStore
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
                mode,
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
