using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeAttachmentUploadTests
{
    private static readonly Guid ChallengeId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AttachmentId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string StoredObjectKey = "normalized/attachment-object";

    [Test]
    public async Task Added_attachment_keeps_the_stored_object()
    {
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<StoredObject>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AddChallengeAttachmentState.Added));

        var result = await UploadAsync(harness);

        await Assert.That(result.Succeeded).IsTrue();
        await harness.Objects.DidNotReceiveWithAnyArgs()
            .DeleteAsync(default!, default);
    }

    [Test]
    [Arguments(AddChallengeAttachmentState.ChallengeNotFound, ChallengeAttachmentFailureCode.ChallengeNotFound)]
    [Arguments(AddChallengeAttachmentState.ResourceIdConflict, ChallengeAttachmentFailureCode.ResourceIdConflict)]
    public async Task Rejected_attachment_deletes_the_exact_stored_object_once(
        AddChallengeAttachmentState state,
        ChallengeAttachmentFailureCode expectedCode)
    {
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<StoredObject>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(state));
        harness.Objects.DeleteAsync(StoredObjectKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup failure")));

        var result = await UploadAsync(harness);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(expectedCode);
        await AssertCompensatedOnceAsync(harness.Objects);
    }

    [Test]
    public async Task Database_failure_deletes_the_exact_stored_object_once()
    {
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<StoredObject>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddChallengeAttachmentState>(
                new DbUpdateException("database write failed")));

        var action = async () => await UploadAsync(harness);

        var exception = await Assert.That(action).Throws<DbUpdateException>();
        await Assert.That(exception!.Message).IsEqualTo("database write failed");
        await AssertCompensatedOnceAsync(harness.Objects);
    }

    [Test]
    public async Task Cleanup_failure_does_not_mask_the_store_failure()
    {
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<StoredObject>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddChallengeAttachmentState>(
                new AttachmentStoreException("primary store failure")));
        harness.Objects.DeleteAsync(StoredObjectKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup failure")));

        var action = async () => await UploadAsync(harness);

        var exception = await Assert.That(action).Throws<AttachmentStoreException>();
        await Assert.That(exception!.Message).IsEqualTo("primary store failure");
        await AssertCompensatedOnceAsync(harness.Objects);
    }

    [Test]
    public async Task Request_cancellation_uses_a_fresh_cleanup_token_without_masking_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<StoredObject>(),
                Arg.Any<DateTimeOffset>(),
                cancellation.Token)
            .Returns(Task.FromCanceled<AddChallengeAttachmentState>(cancellation.Token));
        harness.Objects.DeleteAsync(StoredObjectKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup failure")));

        var action = async () => await UploadAsync(harness, cancellation.Token);

        var exception = await Assert.That(action).Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await AssertCompensatedOnceAsync(harness.Objects);
    }

    private static Harness CreateHarness()
    {
        var store = Substitute.For<IChallengeAttachmentStore>();
        var objects = Substitute.For<IObjectStorage>();
        store.AttachmentIdExistsAsync(AttachmentId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        objects.PutAsync(
                $"challenges/{ChallengeId:N}/attachments/{AttachmentId:N}",
                "attachment.bin",
                "application/octet-stream",
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StoredObject(
                StoredObjectKey,
                "attachment.bin",
                "application/octet-stream",
                3,
                new string('A', 64))));
        return new(store, objects, new ManageChallengeAttachments(store, objects));
    }

    private static async Task<OperationResult<ChallengeAttachmentView, ChallengeAttachmentFailureCode>> UploadAsync(
        Harness harness,
        CancellationToken cancellationToken = default)
    {
        using var content = new MemoryStream([1, 2, 3]);
        return await harness.UseCase.UploadAsync(
            ChallengeId,
            ActorId,
            false,
            AttachmentId,
            "attachment.bin",
            "application/octet-stream",
            content,
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            cancellationToken);
    }

    private static async Task AssertCompensatedOnceAsync(IObjectStorage objects) =>
        await objects.Received(1).DeleteAsync(
            StoredObjectKey,
            Arg.Is<CancellationToken>(token => token == CancellationToken.None));

    private sealed record Harness(
        IChallengeAttachmentStore Store,
        IObjectStorage Objects,
        ManageChallengeAttachments UseCase);

    private sealed class AttachmentStoreException(string message) : Exception(message);
}
