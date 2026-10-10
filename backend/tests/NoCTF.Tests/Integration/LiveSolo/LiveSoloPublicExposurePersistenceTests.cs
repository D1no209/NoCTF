using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloPublicExposurePersistenceTests
{
    private static async Task<Guid> Viewer(LiveSoloMatchPersistenceTests.Fixture f, Guid actor, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(f.Options);
        return (await new LiveSoloViewerStore(db, new CompetitionModerationAuthorizer(db), f.Clock)
            .EnterAsync(f.Competition.Id, f.Match.Id, actor, null, ct)).Admission?.Id ?? Guid.Empty;
    }
    [Test, Timeout(300_000)]
    public async Task Published_metadata_without_a_viewer_persists_immutable_static_risk_even_after_flags_are_removed(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var segment = await Segment(f, ct);
            foreach (var flag in await f.Db.ChallengeFlags.ToArrayAsync(ct)) flag.DeletedAt = f.Now;
            await f.Db.SaveChangesAsync(ct); f.Now = segment.PublicAt.AddTicks(-1);
            var objects = Substitute.For<IStore>(); var reader = Reader(f, objects);
            await reader.ReadAsync(f.Competition.Id, f.Match.Id, Guid.Empty, await Viewer(f, Guid.Empty, ct), ct);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(0);
            f.Now = segment.PublicAt; await reader.ReadAsync(f.Competition.Id, f.Match.Id, Guid.Empty, await Viewer(f, Guid.Empty, ct), ct);
            var exposure = await f.Db.LiveSoloQuestionExposures.SingleAsync(ct);
            await Assert.That(exposure.CanonicalChallengeId).IsEqualTo(f.Templates[0].Id);
            await Assert.That(exposure.PublicAt).IsEqualTo(segment.PublicAt);
            await objects.DidNotReceiveWithAnyArgs().OpenRead(default!, default);
            await reader.ReadAsync(f.Competition.Id, f.Match.Id, Guid.Empty, await Viewer(f, Guid.Empty, ct), ct);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(1);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Private_program_reads_and_dynamic_only_frames_do_not_mark_public_static_exposure(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            foreach (var flag in await f.Db.ChallengeFlags.ToArrayAsync(ct)) f.Db.ChallengeFlags.Remove(flag);
            await f.Db.SaveChangesAsync(ct); var segment = await Segment(f, ct); f.Now = segment.PublicAt;
            var reader = Reader(f, Substitute.For<IStore>());
            await reader.ReadAsync(f.Competition.Id, f.Match.Id, Guid.Empty, await Viewer(f, Guid.Empty, ct), ct);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(0);
            // Create an immutable static frame in a separate capture generation while access remains staff-only.
            (await f.Db.Competitions.SingleAsync(ct)).AccessMode = CompetitionAccessMode.StaffOnly;
            f.Db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = f.Templates[0].Id,
                Flag = "flag{attachment}", FlagSha256 = new byte[32], SpecificationKind = SpecificationKind.Attachment, SpecificationId = Guid.NewGuid(), CreatedAt = f.Now });
            await f.Db.SaveChangesAsync(ct); f.Now = f.Now.AddSeconds(1); await Capture(f).EnsureAsync(segment.MediaSessionId, ct);
            var frame = await f.Db.LiveSoloProgramFrames.OrderByDescending(x => x.OccurredAt).Include(x => x.Questions).FirstAsync(ct);
            await Assert.That(frame.Questions.First().HasStaticAnswer).IsTrue();
            f.Db.LiveSoloProgramSegments.Add(new() { Id = Guid.NewGuid(), MediaSessionId = segment.MediaSessionId, ProgramCaptureId = segment.ProgramCaptureId,
                FrameId = frame.Id, FileId = segment.FileId, Sequence = 2, StartedAt = f.Now, EndedAt = f.Now, PublicAt = f.Now, RemoveAfter = f.Now.AddMinutes(5) });
            await f.Db.SaveChangesAsync(ct); await reader.ReadAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, await Viewer(f, f.Owner.Id, ct), ct);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(0);
        });
    }
    [Test, Timeout(300_000)]
    public async Task An_unstarted_match_cannot_prepare_an_exposed_source_without_any_stream_request(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var segment = await Segment(f, ct);
            f.Now = segment.PublicAt; var (match, _, _) = await AnotherMatch(f, ct);
            var result = await f.Store(f.Db).PrepareRoundAsync(new(f.Competition.Id, match.Id, f.Owner.Id, match.ConcurrencyStamp, null, f.Now), ct);
            await Assert.That(result.Failure).IsEqualTo(LiveSoloFailure.NoSuitableQuestionGroup);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(1);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Publication_during_countdown_cancels_preparation_before_the_first_actual_release(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var segment = await Segment(f, ct);
            f.Now = segment.PublicAt.AddSeconds(-1); var (match, left, right) = await AnotherMatch(f, ct); var store = f.Store(f.Db);
            var prepared = await store.PrepareRoundAsync(new(f.Competition.Id, match.Id, f.Owner.Id, match.ConcurrencyStamp, null, f.Now), ct);
            await Assert.That(prepared.Failure).IsNull(); var round = prepared.Round!;
            var session = new LiveSoloMediaSession { Id = Guid.NewGuid(), MatchId = match.Id, Generation = Guid.NewGuid(), RoomIdentity = Guid.NewGuid().ToString("N"), State = LiveSoloMediaState.Ready, CreatedAt = f.Now,
                Participants = [new() { UserId = left.CaptainId, TeamId = left.Id, Side = LiveSoloSide.Left, Identity = "fresh-left" },
                    new() { UserId = right.CaptainId, TeamId = right.Id, Side = LiveSoloSide.Right, Identity = "fresh-right" }] };
            f.Db.LiveSoloMediaSessions.Add(session); await f.Db.SaveChangesAsync(ct);
            (await f.Db.LiveSoloMatches.SingleAsync(x => x.Id == match.Id, ct)).CurrentMediaSessionId = session.Id; await f.Db.SaveChangesAsync(ct);
            await f.PrepareProgramAsync(session, ct);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation(session.Participants.Select(p => new LiveSoloObservedScreen(p.Identity, LiveSoloScreenState.Sharing, "track", f.Now)).ToArray()));
            var current = (await store.FindAsync(f.Competition.Id, match.Id, f.Owner.Id, true, f.Now, ct))!;
            var ready = await store.ConfirmReadyAsync(f.Competition.Id, match.Id, left.CaptainId, current.ConcurrencyStamp, f.Now, ct);
            await store.ConfirmReadyAsync(f.Competition.Id, match.Id, right.CaptainId, ready.Match!.ConcurrencyStamp, f.Now, ct);
            var countdown = await store.StartCountdownAsync(new(f.Competition.Id, match.Id, round.Id, f.Owner.Id, round.ConcurrencyStamp, f.Now), ct);
            await Assert.That(countdown.Failure).IsNull(); f.Now = f.Now.AddSeconds(5);
            await store.TickAsync(round.Id, countdown.Round!.TimelineRevision, f.Now, ct);
            var actual = await f.Db.LiveSoloRounds.Include(x => x.Questions).SingleAsync(x => x.Id == round.Id, ct);
            await Assert.That(actual.State).IsEqualTo(LiveSoloRoundState.Canceled); await Assert.That(actual.StartedAt).IsNull();
            await Assert.That(actual.Questions.All(x => x.OpenedAt is null)).IsTrue();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Concurrent_metadata_reads_record_one_exposure_and_pruning_does_not_erase_it(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var segment = await Segment(f, ct); f.Now = segment.PublicAt;
            async Task Read()
            {
                await using var db = new NoCtfDbContext(f.Options);
                await new LiveSoloProgramReader(db, new CompetitionModerationAuthorizer(db), Substitute.For<IStore>(), f.Clock).ReadAsync(f.Competition.Id, f.Match.Id, Guid.Empty, await Viewer(f, Guid.Empty, ct), ct);
            }
            await Task.WhenAll(Read(), Read()); await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(1);
            f.Now = segment.RemoveAfter.AddSeconds(1); await Capture(f).PruneAsync(segment.MediaSessionId, ct);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloQuestionExposures.CountAsync(ct)).IsEqualTo(1);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Expired_publication_rows_are_remembered_before_cleanup_without_a_viewer_or_timely_worker(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var segment = await Segment(f, ct);
            f.Now = segment.RemoveAfter.AddSeconds(1); await Capture(f).PruneAsync(segment.MediaSessionId, ct);
            var exposure = await f.Db.LiveSoloQuestionExposures.SingleAsync(ct);
            await Assert.That(exposure.PublicAt).IsEqualTo(segment.PublicAt);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(ct)).IsEqualTo(0);
        });
    }
    private static LiveSoloProgramReader Reader(LiveSoloMatchPersistenceTests.Fixture f, IStore objects) => new(f.Db, new CompetitionModerationAuthorizer(f.Db), objects, f.Clock);
    private static LiveSoloCaptureStore Capture(LiveSoloMatchPersistenceTests.Fixture f)
    {
        var objects = Substitute.For<IStore>(); var messages = Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db, Substitute.For<ILiveSoloEgressGateway>(), Substitute.For<ILiveSoloCaptureFiles>(),
            new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db, messages, NullLogger<ManagedFileUploadRegistry>.Instance), objects), new(), f.Clock, messages, objects, new CompetitionModerationAuthorizer(f.Db));
    }
    private static async Task<LiveSoloProgramSegment> Segment(LiveSoloMatchPersistenceTests.Fixture f, CancellationToken ct)
    {
        await f.PrepareAsync(ct); await f.StartAsync(ct); var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
        await Capture(f).EnsureAsync(session.Id, ct);
        var frame = await f.Db.LiveSoloProgramFrames.OrderByDescending(x => x.OccurredAt).Include(x => x.Questions).FirstAsync(ct);
        var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "exposure-" + Guid.NewGuid().ToString("N"), FileName = "exposure.ts",
            ContentType = "video/mp2t", ByteLength = 4, Sha256 = new byte[32], CreatedAt = f.Now };
        f.Db.Files.Add(file); await f.Db.SaveChangesAsync(ct);
        var segment = new LiveSoloProgramSegment { Id = Guid.NewGuid(), MediaSessionId = session.Id, ProgramCaptureId = session.CurrentProgramCaptureId!.Value,
            FrameId = frame.Id, FileId = file.Id, Sequence = 1, StartedAt = f.Now, EndedAt = f.Now, PublicAt = f.Now.AddSeconds(60), RemoveAfter = f.Now.AddMinutes(10) };
        f.Db.LiveSoloProgramSegments.Add(segment); await f.Db.SaveChangesAsync(ct); return segment;
    }
    private static async Task<(LiveSoloMatchView Match, Team Left, Team Right)> AnotherMatch(LiveSoloMatchPersistenceTests.Fixture f, CancellationToken ct)
    {
        User User(string name) => new() { Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.test", PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = f.Now };
        var a = User("fresh-left"); var b = User("fresh-right");
        Team Team(User user, char token) => new() { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = user.UserName, CaptainId = user.Id,
            MemberIds = [user.Id], InvitationToken = new string(token, 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        var left = Team(a, 'x'); var right = Team(b, 'y'); f.Db.Users.AddRange(a, b); f.Db.Teams.AddRange(left, right); await f.Db.SaveChangesAsync(ct);
        var store = f.Store(f.Db); var created = await store.CreateAsync(new(f.Competition.Id, f.Owner.Id, left.Id, right.Id, 1, f.Now), ct);
        await Assert.That(created.Failure).IsNull();
        var locked = await store.LockRosterAsync(new(f.Competition.Id, created.Match!.Id, a.Id, left.Id, created.Match.ConcurrencyStamp, [a.Id], f.Now), ct);
        var second = await store.LockRosterAsync(new(f.Competition.Id, created.Match.Id, b.Id, right.Id, locked.Match!.ConcurrencyStamp, [b.Id], f.Now), ct);
        await Assert.That(second.Failure).IsNull(); return (second.Match!, left, right);
    }
}
