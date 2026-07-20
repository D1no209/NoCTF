using Microsoft.Extensions.Configuration;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Unit.Infrastructure;

public class ValidatingFixSubmissionVerifierTests
{
    [Test]
    public async Task Verify_MatchingArchive_ReachesRunnerBoundary()
    {
        var archive = Archive();
        var verifier = Create(new Storage
        {
            Object = new StoredObject(archive.ObjectKey, archive.FileName, archive.ContentType, archive.Length, archive.Sha256)
        });

        var result = await verifier.VerifyAsync(Submission(archive), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(FixVerificationDecision.PlatformFailed);
        await Assert.That(result.ErrorCode).IsEqualTo(ScoringFailureCode.StorageUnavailable);
    }

    [Test]
    [Arguments("../escape")]
    [Arguments("/absolute")]
    [Arguments("fix\\escape")]
    public async Task Verify_UnsafeObjectKey_IsRejected(string objectKey)
    {
        var storage = new Storage();
        var verifier = Create(storage);

        var result = await verifier.VerifyAsync(Submission(Archive() with { ObjectKey = objectKey }), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(FixVerificationDecision.TeamFailure);
        await Assert.That(result.ErrorCode).IsEqualTo(ScoringFailureCode.FixArchiveMissing);
        await Assert.That(storage.InspectCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Verify_LengthMismatch_IsTeamFailure()
    {
        var archive = Archive();
        var verifier = Create(new Storage
        {
            Object = new StoredObject(archive.ObjectKey, archive.FileName, archive.ContentType, archive.Length + 1, archive.Sha256)
        });

        var result = await verifier.VerifyAsync(Submission(archive), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(FixVerificationDecision.TeamFailure);
        await Assert.That(result.ErrorCode).IsEqualTo(ScoringFailureCode.FixArchiveLengthMismatch);
    }

    [Test]
    public async Task Verify_HashMismatch_IsTeamFailure()
    {
        var archive = Archive();
        var verifier = Create(new Storage
        {
            Object = new StoredObject(archive.ObjectKey, archive.FileName, archive.ContentType, archive.Length, new string('B', 64))
        });

        var result = await verifier.VerifyAsync(Submission(archive), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(FixVerificationDecision.TeamFailure);
        await Assert.That(result.ErrorCode).IsEqualTo(ScoringFailureCode.FixArchiveHashMismatch);
    }

    [Test]
    public async Task Verify_InspectionTimeout_IsPlatformFailure()
    {
        var verifier = Create(new Storage { WaitForCancellation = true }, 1);

        var result = await verifier.VerifyAsync(Submission(Archive()), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(FixVerificationDecision.PlatformFailed);
        await Assert.That(result.ErrorCode).IsEqualTo(ScoringFailureCode.StorageTimeout);
    }

    private static ValidatingFixSubmissionVerifier Create(Storage storage, int timeoutSeconds = 30) => new(
        storage,
        new UnavailableFixSubmissionVerifier(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FixVerification:ArchiveInspectionTimeoutSeconds"] = timeoutSeconds.ToString()
        }).Build());

    private static FixArchiveReference Archive() =>
        new("fix/competition/upload", "fix.zip", "application/zip", 42, new string('A', 64));

    private static FixSubmissionReceived Submission(FixArchiveReference archive) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "submission-key", archive, "127.0.0.1", DateTimeOffset.UtcNow);

    private sealed class Storage : IObjectStorage
    {
        public StoredObject? Object { get; init; }
        public bool WaitForCancellation { get; init; }
        public int InspectCalls { get; private set; }

        public Task<FixUploadGrant> CreateUploadAsync(Guid uploadId, string objectKey, string contentType, long length, TimeSpan lifetime, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<StoredObject?> InspectAsync(string objectKey, CancellationToken cancellationToken)
        {
            InspectCalls++;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Object;
        }

        public Task<StoredObject> PutAsync(string objectKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
