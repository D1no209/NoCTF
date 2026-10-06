using NSubstitute;
using FluentStorage.Storage;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.Application;

public sealed class AttachmentDownloadEvidenceTests
{
    [Test]
    public async Task Successfully_opened_stream_records_authorized_team_and_actor()
    {
        var store = Substitute.For<IChallengeAttachmentStore>();
        var objects = Substitute.For<IStore>();
        var competition = Guid.NewGuid(); var cc = Guid.NewGuid(); var actor = Guid.NewGuid(); var team = Guid.NewGuid(); var attachment = Guid.NewGuid();
        var metadata = new ChallengeAttachmentView(attachment, Guid.NewGuid(), "a.txt", "text/plain", 1, "hash", null, null, DateTimeOffset.UtcNow);
        store.GetPlayerAsync(competition, cc, attachment, actor, Arg.Any<Func<string, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(new ChallengeAttachmentContent(metadata, "object", team));
        objects.OpenRead("object", Arg.Any<CancellationToken>()).Returns(new MemoryStream([1]));
        var opened = await new GetChallengeAttachments(store, objects).OpenAsync(competition, cc, attachment, actor);
        await Assert.That(opened).IsNotNull();
        await store.Received(1).RecordPlayerDownloadAsync(competition, cc, team, actor, attachment, Arg.Any<CancellationToken>());
        await opened!.Value.Content.DisposeAsync();
    }

    [Test]
    public async Task Stream_is_disposed_if_persisting_evidence_fails()
    {
        var store = Substitute.For<IChallengeAttachmentStore>(); var objects = Substitute.For<IStore>();
        var content = new MemoryStream([1]);
        store.GetPlayerAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid>(),
            Arg.Any<Func<string, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(
            new ChallengeAttachmentContent(new(Guid.NewGuid(), Guid.NewGuid(), "a.txt", "text/plain", 1, "hash", null, null, DateTimeOffset.UtcNow), "object", Guid.NewGuid()));
        objects.OpenRead("object", Arg.Any<CancellationToken>()).Returns(content);
        store.RecordPlayerDownloadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("evidence unavailable")));
        await Assert.That(async () => await new GetChallengeAttachments(store, objects)
            .OpenAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).Throws<IOException>();
        await Assert.That(content.CanRead).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Denied_or_missing_object_stream_does_not_record_download(bool authorized)
    {
        var store = Substitute.For<IChallengeAttachmentStore>(); var objects = Substitute.For<IStore>();
        if (authorized)
            store.GetPlayerAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid>(),
                Arg.Any<Func<string, CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(
                new ChallengeAttachmentContent(new(Guid.NewGuid(), Guid.NewGuid(), "a.txt", "text/plain", 1, "hash", null, null, DateTimeOffset.UtcNow), "missing", Guid.NewGuid()));
        objects.OpenRead(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream?>(null));
        var opened = await new GetChallengeAttachments(store, objects).OpenAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Assert.That(opened).IsNull();
        await store.DidNotReceiveWithAnyArgs().RecordPlayerDownloadAsync(default, default, default, default, default, default);
    }
}
