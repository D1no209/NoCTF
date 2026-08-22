using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;
using System.Security.Cryptography;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeAttachmentUploadTests
{
    private static readonly Guid ChallengeId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AttachmentId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly string StoredObjectKey = $"attachments/{AttachmentId:N}";

    [Test]
    public async Task Added_attachment_keeps_the_stored_object()
    {
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AddChallengeAttachmentState.Added));

        var result = await UploadAsync(harness);

        await Assert.That(result.Succeeded).IsTrue();
        await harness.Registry.DidNotReceiveWithAnyArgs()
            .AbandonAsync(default, default);
    }

    [Test]
    public async Task Unauthorized_challenge_is_rejected_before_file_staging()
    {
        var harness = CreateHarness(canWrite: false);

        var result = await UploadAsync(harness);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeAttachmentFailureCode.ChallengeNotFound);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .AttachmentIdExistsAsync(default, default);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(
            default,
            default!,
            default,
            default);
        await harness.Registry.DidNotReceiveWithAnyArgs()
            .AbandonAsync(default, default);
        await harness.Store.DidNotReceiveWithAnyArgs().AddAsync(
            default,
            default,
            default,
            default,
            default,
            default,
            default);
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
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(state));
        var result = await UploadAsync(harness);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(expectedCode);
        await AssertCompensatedOnceAsync(harness.Registry);
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
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddChallengeAttachmentState>(
                new DbUpdateException("database write failed")));

        var action = async () => await UploadAsync(harness);

        var exception = await Assert.That(action).Throws<DbUpdateException>();
        await Assert.That(exception!.Message).IsEqualTo("database write failed");
        await AssertCompensatedOnceAsync(harness.Registry);
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
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddChallengeAttachmentState>(
                new AttachmentStoreException("primary store failure")));
        harness.Registry.AbandonAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("cleanup failure")));

        var action = async () => await UploadAsync(harness);

        var exception = await Assert.That(action).Throws<AttachmentStoreException>();
        await Assert.That(exception!.Message).IsEqualTo("primary store failure");
        await AssertCompensatedOnceAsync(harness.Registry);
    }

    [Test]
    public async Task Request_cancellation_before_registration_leaves_nothing_to_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var harness = CreateHarness();
        harness.Store.AddAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                cancellation.Token)
            .Returns(Task.FromCanceled<AddChallengeAttachmentState>(cancellation.Token));
        var action = async () => await UploadAsync(harness, cancellation.Token);

        var exception = await Assert.That(action).Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(
            default,
            default!,
            default,
            default);
        await harness.Registry.DidNotReceiveWithAnyArgs().AbandonAsync(default, default);
        await harness.Store.DidNotReceiveWithAnyArgs().AddAsync(
            default,
            default,
            default,
            default,
            default,
            default,
            default);
    }

    [Test]
    public async Task Random_batch_uses_complete_source_names_as_flags_and_one_download_name()
    {
        var harness = CreateHarness();
        harness.Store.AddRandomBatchAsync(
                ChallengeId,
                ActorId,
                false,
                "challenge.zip",
                Arg.Any<IReadOnlyList<RandomAttachmentBatchEntry>>(),
                Arg.Any<CancellationToken>())
            .Returns(AddChallengeAttachmentState.Added);
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            [
                new("flag{one}.zip", "application/zip", first),
                new("flag{two}.tar.gz", "application/gzip", second)
            ],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Items.Select(item => item.ExactFlag!))
            .IsEquivalentTo(["flag{one}.zip", "flag{two}.tar.gz"]);
        await Assert.That(result.Value.Items.All(item => item.FileName == "challenge.zip")).IsTrue();
        await harness.Store.Received(1).AddRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            Arg.Is<IReadOnlyList<RandomAttachmentBatchEntry>>(entries =>
                entries != null && entries.Select(entry => entry.ExactFlag).SequenceEqual(
                    new[] { "flag{one}.zip", "flag{two}.tar.gz" })),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Duplicate_random_batch_flag_is_rejected_before_storage()
    {
        var harness = CreateHarness();
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            [
                new("flag{same}", "application/zip", first),
                new("flag{same}", "application/zip", second)
            ],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode).IsEqualTo(ChallengeAttachmentFailureCode.DuplicateFlag);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default, default);
        await harness.Store.DidNotReceiveWithAnyArgs().AddRandomBatchAsync(
            default, default, default, default!, default!, default);
    }

    [Test]
    [Arguments("../flag{escape}")]
    [Arguments("folder/flag{nested}")]
    [Arguments("folder\\flag{nested}")]
    [Arguments(".")]
    [Arguments("..")]
    public async Task Invalid_random_variant_file_name_is_rejected_before_storage(string fileName)
    {
        var harness = CreateHarness();
        using var content = new MemoryStream([1]);

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            [new(fileName, "application/octet-stream", content)],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeAttachmentFailureCode.InvalidVariantFileName);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default, default);
        await harness.Store.DidNotReceiveWithAnyArgs().AddRandomBatchAsync(
            default, default, default, default!, default!, default);
    }

    [Test]
    [Arguments("../challenge.zip")]
    [Arguments("folder/challenge.zip")]
    [Arguments("folder\\challenge.zip")]
    public async Task Invalid_player_download_file_name_is_rejected_on_every_platform(string fileName)
    {
        var harness = CreateHarness();
        using var content = new MemoryStream([1]);

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            fileName,
            [new("flag{one}", "application/octet-stream", content)],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeAttachmentFailureCode.InvalidFileName);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default, default);
        await harness.Store.DidNotReceiveWithAnyArgs().AddRandomBatchAsync(
            default, default, default, default!, default!, default);
    }

    [Test]
    public async Task Empty_random_batch_is_rejected_before_storage()
    {
        var harness = CreateHarness();

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            [],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode).IsEqualTo(ChallengeAttachmentFailureCode.EmptyBatch);
        await harness.Registry.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default, default);
    }

    [Test]
    public async Task Partial_random_batch_storage_failure_compensates_every_registered_file()
    {
        var harness = CreateHarness();
        var puts = 0;
        harness.Objects.PutAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                puts++;
                return puts == 2
                    ? Task.FromException<StoredObject>(new IOException("storage failed"))
                    : Task.FromResult(new StoredObject(
                        call.ArgAt<string>(0),
                        call.ArgAt<string>(1),
                        call.ArgAt<string>(2),
                        1,
                        Convert.ToHexString(SHA256.HashData([1]))));
            });
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);

        var result = await harness.UseCase.UploadRandomBatchAsync(
            ChallengeId,
            ActorId,
            false,
            "challenge.zip",
            [
                new("flag{one}", "application/zip", first),
                new("flag{two}", "application/zip", second)
            ],
            DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        await Assert.That(result.FailureCode)
            .IsEqualTo(ChallengeAttachmentFailureCode.BatchStorageFailed);
        await harness.Registry.Received(2).AbandonAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await harness.Store.DidNotReceiveWithAnyArgs().AddRandomBatchAsync(
            default, default, default, default!, default!, default);
    }

    private static Harness CreateHarness(bool canWrite = true)
    {
        var store = Substitute.For<IChallengeAttachmentStore>();
        var objects = Substitute.For<IObjectStorage>();
        var registry = Substitute.For<IManagedFileUploadRegistry>();
        store.CanWriteAsync(
                ChallengeId,
                ActorId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(canWrite));
        store.AttachmentIdExistsAsync(AttachmentId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        objects.PutAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var copy = new MemoryStream();
                call.ArgAt<Stream>(3).CopyTo(copy);
                var bytes = copy.ToArray();
                return Task.FromResult(new StoredObject(
                    call.ArgAt<string>(0),
                    call.ArgAt<string>(1),
                    call.ArgAt<string>(2),
                    bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes))));
            });
        return new(
            store,
            objects,
            registry,
            new ManageChallengeAttachments(
                store,
                new ManagedFileUploads(registry, objects)));
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

    private static async Task AssertCompensatedOnceAsync(IManagedFileUploadRegistry registry) =>
        await registry.Received(1).AbandonAsync(
            Arg.Any<Guid>(),
            Arg.Is<CancellationToken>(token => token == CancellationToken.None));

    private sealed record Harness(
        IChallengeAttachmentStore Store,
        IObjectStorage Objects,
        IManagedFileUploadRegistry Registry,
        ManageChallengeAttachments UseCase);

    private sealed class AttachmentStoreException(string message) : Exception(message);
}
