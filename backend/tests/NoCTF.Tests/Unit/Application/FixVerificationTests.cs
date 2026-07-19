using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using ApplicationFixVerificationStatus = NoCTF.Application.Submissions.Processing.FixVerificationDecision;
using DomainFixVerificationStatus = NoCTF.Domain.Submissions.FixVerificationStatus;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public class FixVerificationTests
{
    [Test]
    public async Task ExpireFixUploads_DelegatesCurrentTimeToStore()
    {
        var store = new Store { ExpiredCount = 3 };
        var now = DateTimeOffset.UtcNow;

        var count = await new ExpireFixUploads(store).ExecuteAsync(now);

        await Assert.That(count).IsEqualTo(3);
        await Assert.That(store.ExpiredAt).IsEqualTo(now);
    }
    [Test]
    public async Task StateMachine_AllowsOnlyExpectedTransitions()
    {
        await Assert.That(FixVerificationStateMachine.CanTransition(
            DomainFixVerificationStatus.Claimed, DomainFixVerificationStatus.Verifying)).IsTrue();
        await Assert.That(FixVerificationStateMachine.CanTransition(
            DomainFixVerificationStatus.Verifying, DomainFixVerificationStatus.Valid)).IsTrue();
        await Assert.That(FixVerificationStateMachine.CanTransition(
            DomainFixVerificationStatus.Valid, DomainFixVerificationStatus.Verifying)).IsFalse();
        await Assert.That(FixVerificationStateMachine.CanTransition(
            DomainFixVerificationStatus.Expired, DomainFixVerificationStatus.Valid)).IsFalse();
    }

    [Test]
    [Arguments(DomainFixVerificationStatus.Valid, true)]
    [Arguments(DomainFixVerificationStatus.TeamFailure, true)]
    [Arguments(DomainFixVerificationStatus.PlatformFailed, true)]
    [Arguments(DomainFixVerificationStatus.Created, false)]
    [Arguments(DomainFixVerificationStatus.Claimed, false)]
    [Arguments(DomainFixVerificationStatus.Verifying, false)]
    [Arguments(DomainFixVerificationStatus.Expired, false)]
    [Arguments(DomainFixVerificationStatus.CleanupPending, false)]
    public async Task StateMachine_RetryReset_AllowsOnlyCompletedVerification(
        DomainFixVerificationStatus status,
        bool expected)
    {
        await Assert.That(FixVerificationStateMachine.CanResetForRetry(status)).IsEqualTo(expected);
    }

    [Test]
    public async Task CleanupFixArchives_DeletesClaimedObjectsAndCompletesRecord()
    {
        var item = new FixArchiveCleanupItem(Guid.NewGuid(), "fix/competition/upload", 4);
        var store = new CleanupStore([item]);
        var storage = new CleanupStorage();

        var result = await new CleanupFixArchives(store, storage)
            .ExecuteAsync(10, DateTimeOffset.UtcNow);

        await Assert.That(result.DeletedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(storage.DeletedKeys).Contains(item.ObjectKey);
        await Assert.That(store.CompletedUploadId).IsEqualTo(item.UploadId);
    }

    [Test]
    public async Task CleanupFixArchives_StorageFailure_RemainsPendingForRetry()
    {
        var item = new FixArchiveCleanupItem(Guid.NewGuid(), "fix/competition/upload", 4);
        var store = new CleanupStore([item]);
        var storage = new CleanupStorage { ThrowOnDelete = true };

        var result = await new CleanupFixArchives(store, storage)
            .ExecuteAsync(10, DateTimeOffset.UtcNow);

        await Assert.That(result.DeletedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(store.CompletedUploadId).IsNull();
    }

    [Test]
    public async Task VerifyFixSubmission_ValidResult_CompletesWithVerifierVersion()
    {
        var store = new Store();
        var result = await new VerifyFixSubmission(store, new Verifier(
            new(ApplicationFixVerificationStatus.Valid))).ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Completed).IsTrue();
        await Assert.That(store.Version).IsEqualTo("fix-verifier-v1");
    }

    [Test]
    public async Task VerifyFixSubmission_VerifierException_IsMappedToPlatformFailure()
    {
        var store = new Store();
        var result = await new VerifyFixSubmission(store, new ThrowingVerifier())
            .ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Result!.Status).IsEqualTo(ApplicationFixVerificationStatus.PlatformFailed);
        await Assert.That(store.Result.ErrorCode).IsEqualTo(ScoringFailureCode.StorageUnavailable);
    }

    [Test]
    public async Task VerifyFixSubmission_ConcurrentStateChange_ReturnsConflict()
    {
        var store = new Store { CompleteResult = false };
        var result = await new VerifyFixSubmission(store, new Verifier(
            new(ApplicationFixVerificationStatus.TeamFailure))).ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await Assert.That(result.Error).IsEqualTo(FixVerificationError.Concurrency);
    }

    private sealed class Store : IFixVerificationStore
    {
        public int ExpiredCount { get; init; }
        public DateTimeOffset? ExpiredAt { get; private set; }
        public bool CompleteResult { get; init; } = true;
        public bool Completed { get; private set; }
        public string? Version { get; private set; }
        public FixVerificationResult? Result { get; private set; }

        public Task<FixVerificationContext?> BeginVerifyingAsync(
            Guid submissionId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<FixVerificationContext?>(new(
                submissionId,
                new(
                    submissionId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "idempotency",
                    new("fix/archive.zip", "archive.zip", "application/zip", 10, new string('a', 64)),
                    "redacted",
                    now),
                4));

        public Task<bool> CompleteAsync(
            Guid submissionId,
            long expectedRowVersion,
            FixVerificationResult result,
            string verifierVersion,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            Completed = CompleteResult;
            Version = verifierVersion;
            Result = result;
            return Task.FromResult(CompleteResult);
        }

        public Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            ExpiredAt = now;
            return Task.FromResult(ExpiredCount);
        }
    }

    private sealed class Verifier(FixVerificationResult result) : IFixSubmissionVerifier
    {
        public Task<FixVerificationResult> VerifyAsync(
            FixSubmissionReceived submission,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class CleanupStore(IReadOnlyList<FixArchiveCleanupItem> items) : IFixArchiveCleanupStore
    {
        public Guid? CompletedUploadId { get; private set; }

        public Task<IReadOnlyList<FixArchiveCleanupItem>> ClaimAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(items);

        public Task<bool> CompleteAsync(Guid uploadId, long expectedRowVersion, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CompletedUploadId = uploadId;
            return Task.FromResult(true);
        }
    }

    private sealed class CleanupStorage : IObjectStorage
    {
        public bool ThrowOnDelete { get; init; }
        public List<string> DeletedKeys { get; } = [];

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            if (ThrowOnDelete) throw new IOException("storage unavailable");
            DeletedKeys.Add(objectKey);
            return Task.CompletedTask;
        }

        public Task<FixUploadGrant> CreateUploadAsync(Guid uploadId, string objectKey, string contentType, long length, TimeSpan lifetime, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingVerifier : IFixSubmissionVerifier
    {
        public Task<FixVerificationResult> VerifyAsync(
            FixSubmissionReceived submission,
            CancellationToken cancellationToken) => throw new InvalidOperationException("verifier failed");
    }
}
