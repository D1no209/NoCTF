using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FluentStorage.Storage;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloCapturePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Capture_rows_cover_all_roster_tracks_and_a_replacement_track_gets_its_own_recording(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct);
            var session = await Session(fixture, ct); session.RecordingEnabled = true; session.CurrentProgramCaptureId = null;
            foreach (var member in session.Participants) { member.ScreenState = LiveSoloScreenState.Sharing; member.ScreenTrackId = "track-" + member.Identity; }
            await fixture.Db.SaveChangesAsync(ct); fixture.Now = fixture.Now.AddSeconds(2);
            var store = Store(fixture, Substitute.For<ILiveSoloEgressGateway>());
            await store.EnsureAsync(session.Id, ct); await store.EnsureAsync(session.Id, ct);
            await Assert.That(await fixture.Db.LiveSoloRecordings.CountAsync(ct)).IsEqualTo(session.Participants.Count);
            var id = session.CurrentProgramCaptureId;
            await Assert.That(id).IsNotNull();
            session.Participants[0].ScreenTrackId = "replacement-track"; await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = fixture.Now.AddSeconds(2); await store.EnsureAsync(session.Id, ct);
            await Assert.That(await fixture.Db.LiveSoloRecordings.CountAsync(ct)).IsEqualTo(session.Participants.Count + 1);
            await Assert.That(session.CurrentProgramCaptureId).IsEqualTo(id);
            var frame = await fixture.Db.LiveSoloProgramFrames.OrderByDescending(x => x.OccurredAt).FirstAsync(ct);
            await Assert.That(frame.MatchState).IsEqualTo(LiveSoloMatchState.Preparing);
            frame.LeftWins = 99;
            await Assert.That(async () => await fixture.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Unknown_start_outcome_is_reconciled_by_request_identity_without_sending_another_start(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct);
            var session = await Session(fixture, ct); session.CurrentProgramCaptureId = null;
            foreach (var member in session.Participants) member.ScreenState = LiveSoloScreenState.Sharing;
            await fixture.Db.SaveChangesAsync(ct); fixture.Now = fixture.Now.AddSeconds(2);
            var gateway = Substitute.For<ILiveSoloEgressGateway>(); var store = Store(fixture, gateway);
            gateway.ListAsync(session.RoomIdentity, ct).Returns([]);
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(), ct).Returns(Task.FromException<LiveSoloExportObservation>(new HttpRequestException("Response lost")));
            await store.AdvanceAsync(session.Id, ct);
            var program = await fixture.Db.LiveSoloProgramCaptures.SingleAsync(x => x.Id == session.CurrentProgramCaptureId, ct);
            await Assert.That(program.State).IsEqualTo(LiveSoloCaptureState.RequiresReview);
            gateway.ListAsync(session.RoomIdentity, ct).Returns([new("recovered-egress", session.RoomIdentity, LiveSoloExportState.Active,
                fixture.Now, null, null, [], program.Id)]);
            await store.AdvanceAsync(session.Id, ct);
            await Assert.That(program.EgressId).IsEqualTo("recovered-egress");
            await Assert.That(program.State).IsEqualTo(LiveSoloCaptureState.Active);
            await gateway.Received(1).StartAsync(Arg.Any<LiveSoloExportRequest>(), ct);
        });
    }
    [Test, Timeout(300_000)]
    public async Task A_room_with_sharing_roster_and_healthy_dependencies_cannot_start_without_an_actual_program_segment(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct);
            var session = await Session(fixture, ct); session.CurrentProgramCaptureId = null; await fixture.Db.SaveChangesAsync(ct);
            var current = (await fixture.Store(fixture.Db).FindAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Owner.Id, true, fixture.Now, ct))!;
            foreach (var roster in current.Rosters)
                current = (await fixture.Store(fixture.Db).ConfirmReadyAsync(fixture.Competition.Id, current.Id, roster.UserIds.Single(), current.ConcurrencyStamp, fixture.Now, ct)).Match!;
            var result = await fixture.Store(fixture.Db).StartCountdownAsync(new(fixture.Competition.Id, current.Id, fixture.Round.Id,
                fixture.Owner.Id, fixture.Round.ConcurrencyStamp, fixture.Now), ct);
            await Assert.That(result.Failure).IsEqualTo(NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure.MediaUnavailable);
        });
    }
    private static Task<LiveSoloMediaSession> Session(LiveSoloMatchPersistenceTests.Fixture fixture, CancellationToken ct) =>
        fixture.Db.LiveSoloMediaSessions.Include(x => x.Participants).SingleAsync(x => x.Id == fixture.Db.LiveSoloMatches.Select(m => m.CurrentMediaSessionId).Single(), ct);
    [Test, Timeout(300_000)]
    public async Task Imported_video_keeps_first_publication_time_and_the_historical_frame_when_current_match_state_changes(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct);
            var session = await Session(fixture, ct); session.CurrentProgramCaptureId = null; session.PublicDelaySeconds = 60;
            foreach (var member in session.Participants) member.ScreenState = LiveSoloScreenState.Sharing;
            await fixture.Db.SaveChangesAsync(ct); fixture.Now = fixture.Now.AddSeconds(2);
            var gateway = Substitute.For<ILiveSoloEgressGateway>(); gateway.ListAsync(session.RoomIdentity, ct).Returns([]);
            var files = Substitute.For<ILiveSoloCaptureFiles>();
            files.SegmentsAsync(Arg.Any<Guid>(), ct).Returns([new(0, "program_00000.ts", TimeSpan.FromSeconds(2))]);
            files.OpenAsync(Arg.Any<Guid>(), "program_00000.ts", ct).Returns(_ => Task.FromResult<Stream?>(new MemoryStream([0x47, 1, 2, 3])));
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(), ct).Returns(call =>
            {
                var request = call.Arg<LiveSoloExportRequest>() ?? throw new InvalidOperationException("Missing capture request.");
                return Task.FromResult(new LiveSoloExportObservation("video-job", session.RoomIdentity, LiveSoloExportState.Active,
                    fixture.Now.AddSeconds(-2), null, null, [], request.Id));
            });
            var store = Store(fixture, gateway, files); await store.AdvanceAsync(session.Id, ct);
            var segment = await fixture.Db.LiveSoloProgramSegments.SingleAsync(x => x.ProgramCaptureId == session.CurrentProgramCaptureId, ct);
            var publicAt = segment.PublicAt; var frameId = segment.FrameId;
            await Assert.That(publicAt).IsGreaterThanOrEqualTo(fixture.Now.AddSeconds(60));
            var frame = await fixture.Db.LiveSoloProgramFrames.SingleAsync(x => x.Id == frameId, ct);
            await Assert.That(frame.LeftWins).IsEqualTo(0);
            (await fixture.Db.LiveSoloMatches.SingleAsync(ct)).LeftWins = 2; await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = fixture.Now.AddSeconds(10);
            gateway.ListAsync(session.RoomIdentity, ct).Returns([new("video-job", session.RoomIdentity, LiveSoloExportState.Active,
                frame.OccurredAt.AddSeconds(-2), null, null, [], session.CurrentProgramCaptureId)]);
            await store.AdvanceAsync(session.Id, ct);
            await Assert.That(segment.PublicAt).IsEqualTo(publicAt); await Assert.That(segment.FrameId).IsEqualTo(frameId);
            await Assert.That(frame.LeftWins).IsEqualTo(0);
            await Assert.That(await fixture.Db.LiveSoloProgramSegments.CountAsync(x => x.ProgramCaptureId == session.CurrentProgramCaptureId, ct)).IsEqualTo(1);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Retention_preserves_disputed_recordings_and_pending_deletion_is_recoverable(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct);
            var session = await Session(fixture, ct);
            var file = new NoCTF.Domain.Storage.StoredFile { Id = Guid.NewGuid(), ObjectKey = "recording-object", FileName = "recording.mp4", ContentType = "video/mp4",
                ByteLength = 100, Sha256 = new byte[32], CreatedAt = fixture.Now };
            fixture.Db.Files.Add(file);
            var record = new LiveSoloRecording { Id = Guid.NewGuid(), MediaSessionId = session.Id, UserId = fixture.Left.Id, VideoTrackId = "recorded-track",
                State = LiveSoloRecordingState.Completed, FileId = file.Id, CreatedAt = fixture.Now, KeepUntil = fixture.Now.AddSeconds(-1), DisputeHold = true };
            fixture.Db.LiveSoloRecordings.Add(record); await fixture.Db.SaveChangesAsync(ct);
            var captureFiles = Substitute.For<ILiveSoloCaptureFiles>(); var objects = Substitute.For<IStore>();
            var store = Store(fixture, Substitute.For<ILiveSoloEgressGateway>(), captureFiles, objects);
            await store.PruneAsync(session.Id, ct);
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Completed);
            await captureFiles.DidNotReceive().RemoveAsync(record.Id, ct);
            record.DisputeHold = false; await fixture.Db.SaveChangesAsync(ct); await store.PruneAsync(session.Id, ct);
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Deleting);
            captureFiles.RemoveAsync(record.Id, ct).Returns(Task.FromException(new IOException("Temporary storage failure")));
            await Assert.That(async () => await store.FinishPruneAsync(record.Id, ct)).Throws<IOException>();
            await Assert.That(await fixture.Db.LiveSoloRecordings.AnyAsync(x => x.Id == record.Id, ct)).IsTrue();
            captureFiles.RemoveAsync(record.Id, ct).Returns(Task.CompletedTask);
            await store.FinishPruneAsync(record.Id, ct);
            await Assert.That(await fixture.Db.LiveSoloRecordings.AnyAsync(x => x.Id == record.Id, ct)).IsFalse();
            await Assert.That(await fixture.Db.Files.AnyAsync(x => x.Id == file.Id, ct)).IsFalse();
            await objects.Received(1).DeleteObject("recording-object", ct);
        });
    }
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture fixture, ILiveSoloEgressGateway gateway, ILiveSoloCaptureFiles? captureFiles = null, IStore? objectStore = null)
    {
        var objects = objectStore ?? Substitute.For<IStore>(); var publisher = Substitute.For<IPostCommitMessagePublisher>();
        var registry = new ManagedFileUploadRegistry(fixture.Db, publisher, NullLogger<ManagedFileUploadRegistry>.Instance);
        var files = captureFiles ?? Substitute.For<ILiveSoloCaptureFiles>();
        if (captureFiles is null) files.SegmentsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        return new(fixture.Db, gateway, files, new ManagedFileUploads(registry, objects), new LiveKitMediaOptions(), fixture.Clock, publisher, objects, new NoCTF.Infrastructure.Teams.Moderation.CompetitionModerationAuthorizer(fixture.Db));
    }
}
