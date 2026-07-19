using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using DomainSubmissionKind = NoCTF.Domain.Submissions.SubmissionKind;

namespace NoCTF.Tests.Unit.Application;

public class SubmissionIntakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SubmitFlag_ReusedKeyForSameScope_ReturnsOriginalSubmission()
    {
        var originalId = Guid.NewGuid();
        var originalTime = Now.AddMinutes(-1);
        var store = new Store
        {
            Existing = new(SubmissionAcceptanceState.Existing, originalId, originalTime)
        };

        var result = await new SubmitFlag(store, new Policy()).ExecuteAsync(FlagCommand());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.SubmissionId).IsEqualTo(originalId);
        await Assert.That(result.Value.ReceivedAt).IsEqualTo(originalTime);
        await Assert.That(store.AcceptCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SubmitFlag_ReusedKeyForDifferentScope_ReturnsConflict()
    {
        var store = new Store { Existing = new(SubmissionAcceptanceState.IdempotencyConflict) };

        var result = await new SubmitFlag(store, new Policy()).ExecuteAsync(FlagCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("idempotency_conflict");
    }

    [Test]
    public async Task SubmitFlag_MaxAttemptsReached_IsRejectedBeforeWrite()
    {
        var store = new Store { Snapshot = Snapshot() with { AcceptedFlagAttempts = 2 } };

        var result = await new SubmitFlag(store, new Policy(maxFlagAttempts: 2)).ExecuteAsync(FlagCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("attempts_exhausted");
        await Assert.That(store.AcceptCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SubmitFlag_UnsupportedKind_IsRejected()
    {
        var store = new Store();

        var result = await new SubmitFlag(store, new Policy(allowsFlag: false)).ExecuteAsync(FlagCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("submission_kind_unsupported");
    }

    [Test]
    public async Task SubmitFlag_Accepted_PassesIdempotencyKeyAndLimitToAtomicStore()
    {
        var store = new Store();
        var command = FlagCommand() with { IdempotencyKey = "request-42" };

        var result = await new SubmitFlag(store, new Policy(maxFlagAttempts: 5)).ExecuteAsync(command);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastFlag!.IdempotencyKey).IsEqualTo("request-42");
        await Assert.That(store.LastMaxAttempts).IsEqualTo(5);
    }

    [Test]
    public async Task SubmitFix_ReusedKey_ReturnsOriginalBeforeReadingUploadMetadata()
    {
        var originalId = Guid.NewGuid();
        var store = new Store
        {
            Existing = new(SubmissionAcceptanceState.Existing, originalId, Now)
        };

        var result = await new SubmitFix(store, new ThrowingUploadStore(), new Policy(allowsFix: true))
            .ExecuteAsync(FixCommand());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.SubmissionId).IsEqualTo(originalId);
    }

    private static FlagSubmissionCommand FlagCommand() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "flag{value}", "request-1", "127.0.0.1", Now);

    private static FixSubmissionCommand FixCommand() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "request-1", "127.0.0.1", Now);

    private static SubmissionAdmissionSnapshot Snapshot() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GameMode.Ctf, 1, 2, "{}", "{}", 0, 0,
        CompetitionStatus.Running, Now.AddHours(-1), Now.AddHours(1),
        false, false, true, false, false, true, true);

    private sealed class Policy(
        bool allowsFlag = true,
        bool allowsFix = false,
        int? maxFlagAttempts = null,
        int? maxFixAttempts = null) : ISubmissionAdmissionModePolicy
    {
        public SubmissionAdmissionRules GetRules(GameMode mode, string competitionConfigurationJson,
            string challengeConfigurationJson) =>
            new(allowsFlag, allowsFix, maxFlagAttempts, maxFixAttempts);
    }

    private sealed class Store : ISubmissionIntakeStore
    {
        public SubmissionAcceptanceResult? Existing { get; init; }
        public SubmissionAdmissionSnapshot Snapshot { get; init; } = SubmissionIntakeTests.Snapshot();
        public int AcceptCalls { get; private set; }
        public int? LastMaxAttempts { get; private set; }
        public FlagSubmissionReceived? LastFlag { get; private set; }

        public Task<SubmissionAcceptanceResult?> FindAcceptedAsync(
            Guid competitionId, string idempotencyKey, Guid teamId, Guid challengeId, Guid userId,
            DomainSubmissionKind kind, CancellationToken cancellationToken) => Task.FromResult(Existing);

        public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId, Guid teamId, Guid challengeId, Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<SubmissionAdmissionSnapshot?>(Snapshot);

        public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
            FlagSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
            CancellationToken cancellationToken)
        {
            AcceptCalls++;
            LastFlag = received;
            LastMaxAttempts = maxAttempts;
            return Task.FromResult(new SubmissionAcceptanceResult(
                SubmissionAcceptanceState.Created, received.SubmissionId, received.ReceivedAt));
        }

        public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
            FixSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingUploadStore : IFixUploadSessionStore
    {
        public Task<FixUploadGrant?> CreateAsync(CreateFixUploadCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
            Guid uploadId, Guid competitionId, Guid teamId, Guid challengeId, Guid userId,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Upload metadata must not be read for an idempotent replay.");
    }
}
