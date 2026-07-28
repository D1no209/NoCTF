using NoCTF.Application.Submissions.Intake;
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

        await Assert.That(result.ErrorCode).IsEqualTo("flag_invalid");
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

    private sealed class Store : ISubmissionIntakeStore
    {
        public int BatchWrites { get; private set; }
        public IReadOnlyList<string> LastFlags { get; private set; } = [];

        public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SubmissionAdmissionSnapshot?>(new(
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

        public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
            FlagSubmissionReceived received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The batch path must be used.");

        public Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(
            IReadOnlyList<FlagSubmissionReceived> received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken)
        {
            BatchWrites++;
            LastFlags = received.Select(item => item.SubmittedFlag).ToArray();
            return Task.FromResult<IReadOnlyList<SubmissionAcceptanceResult>>(
                received.Select(item => new SubmissionAcceptanceResult(
                    SubmissionAcceptanceState.Created,
                    item.SubmissionId,
                    item.ReceivedAt)).ToArray());
        }

        public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
            FixSubmissionReceived received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class Policy : ISubmissionAdmissionModePolicy
    {
        public SubmissionAdmissionRules GetRules(
            GameMode mode,
            string competitionConfigurationJson,
            string challengeConfigurationJson) =>
            new(true, false, null, null);
    }
}
