using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloAttachmentPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Only_an_authorized_successfully_opened_stream_records_scope_evidence_and_failed_rechecks_dispose_it(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var attachment = Attachment(fixture.Templates[0].Id, fixture.Now, "ordinary", "attachment.bin");
            fixture.Db.Add(attachment); await fixture.Db.SaveChangesAsync(ct);
            await fixture.PrepareAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var request = new LiveSoloResourceRequest(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id, fixture.Left.Id, fixture.Now);
            var objects = Substitute.For<IStore>();
            var store = Store(fixture.Db, fixture.Now); var access = new AccessLiveSoloAttachments(store, objects);
            await Assert.That(await access.ListAsync(request, ct)).IsNull();
            await Assert.That((await access.OpenAsync(request, attachment.Id, ct)).Failure).IsEqualTo(LiveSoloFailure.NotFound);
            await objects.DidNotReceive().OpenRead(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await fixture.StartAsync(ct); request = request with { Now = fixture.Now };
            store = Store(fixture.Db, fixture.Now); access = new(store, objects);
            await Assert.That((await access.ListAsync(request, ct))!.Items.Single().Id).IsEqualTo(attachment.Id);
            await Assert.That(await access.PrepareBrowserAsync(request, attachment.Id, ct)).IsTrue();
            await Assert.That(await fixture.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await access.OpenAsync(request, attachment.Id, ct)).Failure).IsEqualTo(LiveSoloFailure.NotFound);
            objects.ObjectExists(Arg.Any<string>(), ct).Returns(true);
            objects.OpenRead(Arg.Any<string>(), ct).Returns(Task.FromException<Stream>(new IOException("Storage unavailable")));
            await Assert.That(async () => await access.OpenAsync(request, attachment.Id, ct)).Throws<IOException>();
            await Assert.That(await fixture.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
            var stream = new ObservedStream();
            objects.OpenRead(Arg.Any<string>(), ct).Returns(async _ =>
            {
                fixture.LeftTeam.IsBanned = true; await fixture.Db.SaveChangesAsync(ct); return (Stream)stream;
            });
            await Assert.That((await access.OpenAsync(request, attachment.Id, ct)).Failure).IsEqualTo(LiveSoloFailure.NotFound);
            await Assert.That(stream.Disposed).IsTrue();
            await Assert.That(await fixture.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
            fixture.LeftTeam.IsBanned = false; await fixture.Db.SaveChangesAsync(ct);
            objects.OpenRead(Arg.Any<string>(), ct).Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4])));
            var success = await access.OpenAsync(request, attachment.Id, ct);
            await Assert.That(success.Failure).IsNull(); await success.Content!.DisposeAsync();
            await Assert.That(await fixture.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(1);
            var fact = await fixture.Db.GameplayFacts.SingleAsync(x => x.Kind == GameplayFactKind.AttachmentDownload, ct);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Applied);
            await Assert.That(await new ChallengeAttachmentStore(fixture.Db).ListPlayerAsync(fixture.Competition.Id, question.CompetitionChallengeId, fixture.Left.Id, ct)).IsNull();
            var accepted = await fixture.Store(fixture.Db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{first}", fixture.Now), ct);
            var snapshot = await fixture.Db.GameplayFacts.Include(x => x.AcquisitionEvidence).SingleAsync(x => x.Id == accepted.GameplayFactId, ct);
            await Assert.That(snapshot.AcquisitionEvidence!.AttachmentDownloadFactId).IsEqualTo(fact.Id);
            await Assert.That(snapshot.AcquisitionEvidence.MissingEvidence).IsNull();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Random_choices_are_shared_by_locked_team_members_and_other_teams_or_rounds_cannot_supply_ownership(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var member = new User { Id = Guid.NewGuid(), UserName = "attachment-member", NormalizedUserName = "ATTACHMENT-MEMBER", Email = "member@example.test",
                PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = fixture.Now };
            fixture.Db.Users.Add(member); fixture.LeftTeam.MemberIds = [fixture.Left.Id, member.Id];
            var a = Attachment(fixture.Templates[0].Id, fixture.Now, "a", "variant.bin");
            var b = Attachment(fixture.Templates[0].Id, fixture.Now, "b", "variant.bin");
            fixture.Db.AddRange(a, b);
            fixture.Db.ChallengeFlags.RemoveRange(await fixture.Db.ChallengeFlags.Where(x => x.ChallengeId == fixture.Templates[0].Id).ToArrayAsync(ct));
            foreach (var file in new[] { a, b }) fixture.Db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = fixture.Templates[0].Id,
                Flag = "flag{" + file.Id + "}", FlagSha256 = ManageChallengeFlags.Hash("flag{" + file.Id + "}"),
                SpecificationKind = SpecificationKind.Attachment, SpecificationId = file.Id, CreatedAt = fixture.Now });
            await fixture.Db.SaveChangesAsync(ct); await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var request = new LiveSoloResourceRequest(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id, fixture.Left.Id, fixture.Now);
            var before = await Store(fixture.Db, fixture.Now).ListAsync(request, ct);
            await Assert.That(before!.DeliveryPolicy).IsEqualTo(AttachmentDeliveryPolicy.RandomOnePerTeam);
            await Assert.That(before.Items).IsEmpty(); await Assert.That(await fixture.Db.LiveSoloAttachmentAssignments.CountAsync(ct)).IsEqualTo(0);
            var selected = await Task.WhenAll(Enumerable.Range(0, 3).Select(async i =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await Store(db, fixture.Now).SelectAsync(request with { ActorId = i % 2 == 0 ? fixture.Left.Id : member.Id }, null, ct);
            }));
            await Assert.That(selected.All(x => x is not null)).IsTrue();
            await Assert.That(selected.Select(x => x!.Metadata.Id).Distinct().Count()).IsEqualTo(1);
            await Assert.That(await fixture.Db.LiveSoloAttachmentAssignments.CountAsync(ct)).IsEqualTo(1);
            var right = await Store(fixture.Db, fixture.Now).SelectAsync(request with { ActorId = fixture.Right.Id }, null, ct);
            await Assert.That(right!.Metadata.Id).IsNotEqualTo(selected[0]!.Metadata.Id);
            await Assert.That(await fixture.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
            var objects = Substitute.For<IStore>(); objects.ObjectExists(Arg.Any<string>(), ct).Returns(true);
            objects.OpenRead(Arg.Any<string>(), ct).Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4])));
            var downloaded = await new AccessLiveSoloAttachments(Store(fixture.Db, fixture.Now), objects).OpenAsync(request with { ActorId = member.Id }, null, ct);
            await Assert.That(downloaded.Failure).IsNull(); await downloaded.Content!.DisposeAsync();
            var foreignFlag = "flag{" + right.Metadata.Id + "}";
            var foreign = await fixture.Store(fixture.Db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, foreignFlag, fixture.Now), ct);
            var own = await fixture.Store(fixture.Db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, question.Id,
                fixture.Left.Id, "flag{" + selected[0]!.Metadata.Id + "}", fixture.Now), ct);
            await fixture.Store(fixture.Db).ResolveAsync(fixture.Round.Id, fixture.Now, ct);
            var rejected = await fixture.Db.GameplayFacts.SingleAsync(x => x.Id == foreign.GameplayFactId, ct);
            await Assert.That(rejected.FailureCode).IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
            await Assert.That(rejected.VictimTeamId).IsEqualTo(fixture.RightTeam.Id);
            await Assert.That((await fixture.Db.LiveSoloRounds.SingleAsync(x => x.Id == fixture.Round.Id, ct)).WinningGameplayFactId).IsEqualTo(own.GameplayFactId);
            await Assert.That(await Store(fixture.Db, fixture.Now).SelectAsync(request with { RoundId = Guid.NewGuid() }, null, ct)).IsNull();
            await Assert.That(await Store(fixture.Db, fixture.Now).SelectAsync(request, null, ct)).IsNull();
        });
    }

    private static LiveSoloAttachmentStore Store(NoCtfDbContext db, DateTimeOffset now) => new(db, new LiveSoloExecutionAccess(db), new FakeTimeProvider(now));
    private static ChallengeAttachment Attachment(Guid template, DateTimeOffset now, string key, string name) => new() { Id = Guid.NewGuid(), ChallengeId = template,
        CreatedAt = now, File = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "livesolo/" + key, FileName = name,
            ContentType = "application/octet-stream", ByteLength = 4, Sha256 = new byte[32], CreatedAt = now } };
    private sealed class ObservedStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public override ValueTask DisposeAsync() { Disposed = true; return base.DisposeAsync(); }
    }
}
