using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloRecordingReservationPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Recording_enabled_countdown_requires_real_active_recorders_for_every_roster_member(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var session=await Sharing(f,ct);
            var store=f.Store(f.Db);var left=await store.ConfirmReadyAsync(f.Competition.Id,f.Match.Id,f.Left.Id,f.Match.ConcurrencyStamp,f.Now,ct);
            await store.ConfirmReadyAsync(f.Competition.Id,f.Match.Id,f.Right.Id,left.Match!.ConcurrencyStamp,f.Now,ct);
            var command=new NoCTF.Application.LiveSolo.Matches.StartLiveSoloCountdown(f.Competition.Id,f.Match.Id,f.Round.Id,f.Owner.Id,f.Round.ConcurrencyStamp,f.Now);
            await Assert.That((await store.StartCountdownAsync(command,ct)).Failure).IsEqualTo(NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure.MediaUnavailable);
            var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);var jobs=new List<LiveSoloExportObservation>{new(program.EgressId!,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],program.Id)};
            foreach(var member in session.Participants){var record=new LiveSoloRecording {Id=Guid.NewGuid(),MediaSessionId=session.Id,UserId=member.UserId,VideoTrackId=member.ScreenTrackId!,
                State=LiveSoloRecordingState.Recording,ReservedBytes=48L*1024*1024,EgressId="record-"+member.Identity,CreatedAt=f.Now,KeepUntil=f.Now.AddDays(30)};f.Db.LiveSoloRecordings.Add(record);
                jobs.Add(new(record.EgressId,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],record.Id));}
            await f.Db.SaveChangesAsync(ct);f.egress.ListAsync(session.RoomIdentity,ct).Returns(_=>jobs.ToArray());
            jobs[1]=jobs[1] with {State=LiveSoloExportState.Failed};await Assert.That((await store.StartCountdownAsync(command,ct)).Failure).IsEqualTo(NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure.MediaUnavailable);
            jobs[1]=jobs[1] with {State=LiveSoloExportState.Active};await Assert.That((await store.StartCountdownAsync(command,ct)).Failure).IsNull();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Concurrent_recording_starts_reserve_capacity_before_RPC_and_cannot_reset_the_global_budget(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var session=await Sharing(f,ct);
            var options=new LiveKitMediaOptions {RecordingExportLimitBytes=16L*1024*1024,RecordingQuotaBytes=48L*1024*1024};
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,Arg.Any<CancellationToken>()).Returns([]);
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),Arg.Any<CancellationToken>()).Returns(call=>{
                var request=call.Arg<LiveSoloExportRequest>()!;return new LiveSoloExportObservation("job-"+request.Id,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],request.Id);});
            var files=Files();
            async Task Advance(){await using var db=new NoCtfDbContext(f.Options);await Store(db,f,gateway,files,options).AdvanceAsync(session.Id,ct);}
            await Task.WhenAll(Advance(),Advance());
            await Assert.That(await f.Db.LiveSoloRecordings.SumAsync(x=>x.ReservedBytes,ct)).IsEqualTo(48L*1024*1024);
            await Assert.That(await f.Db.LiveSoloRecordings.CountAsync(x=>x.State==LiveSoloRecordingState.RequiresReview,ct)).IsEqualTo(1);
            await gateway.Received(1).StartAsync(Arg.Is<LiveSoloExportRequest>(x=>x!=null&&x.Kind==LiveSoloExportKind.ScreenRecording),Arg.Any<CancellationToken>());
            await Store(f.Db,f,gateway,files,options).EnsureAsync(session.Id,ct);
            await Assert.That(await f.Db.LiveSoloRecordings.CountAsync(ct)).IsEqualTo(2);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Size_stop_import_and_raw_cleanup_allow_a_new_UUID_chunk_on_the_same_track(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var session=await Sharing(f,ct);
            var firstMember=session.Participants[0];session.Participants[1].ScreenState=LiveSoloScreenState.Disconnected;await f.Db.SaveChangesAsync(ct);
            var options=new LiveKitMediaOptions {RecordingExportLimitBytes=16L*1024*1024,RecordingQuotaBytes=128L*1024*1024};
            var jobs=new List<LiveSoloExportObservation>();var gateway=Substitute.For<ILiveSoloEgressGateway>();
            gateway.ListAsync(session.RoomIdentity,ct).Returns(_=>jobs.ToArray());
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(call=>{
                var req=call.Arg<LiveSoloExportRequest>()!;var job=new LiveSoloExportObservation("chunk-"+req.Id,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],req.Id);jobs.Add(job);return job;});
            var files=Files();files.RecordingLengthAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(13L*1024*1024);
            var store=Store(f.Db,f,gateway,files,options);await store.AdvanceAsync(session.Id,ct);
            var record=await f.Db.LiveSoloRecordings.SingleAsync(ct);await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Finalizing);
            await gateway.Received(1).StopAsync(record.EgressId!,ct);
            jobs[0]=jobs[0] with {State=LiveSoloExportState.Complete,EndedAt=f.Now,Files=[new("/out/live-solo/"+record.Id.ToString("N")+"/recording.mp4",4)]};
            files.OpenAsync(record.Id,"recording.mp4",ct).Returns(_=>Task.FromResult<Stream?>(new MemoryStream([1,2,3,4])));
            await store.AdvanceAsync(session.Id,ct);await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Completed);await Assert.That(record.ReservedBytes).IsEqualTo(0);
            await store.RemoveRecordingRawAsync(record.Id,ct);await Assert.That(record.RawRemovedAt).IsNotNull();
            await files.Received(1).RemoveRecordingStagingAsync(record.EgressId!,ct);
            f.Now=f.Now.AddSeconds(2);await store.EnsureAsync(session.Id,ct);
            var next=await f.Db.LiveSoloRecordings.SingleAsync(x=>x.Chunk==1,ct);
            await Assert.That(next.Id).IsNotEqualTo(record.Id);await Assert.That(next.VideoTrackId).IsEqualTo(firstMember.ScreenTrackId);
            await Assert.That(next.State).IsEqualTo(LiveSoloRecordingState.Pending);await Assert.That(record.FileId).IsNotNull();
        });
    }
    private static ILiveSoloCaptureFiles Files(){var files=Substitute.For<ILiveSoloCaptureFiles>();files.SegmentsAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns([]);return files;}
    [Test,Timeout(300_000)]
    public async Task Failed_export_keeps_capacity_until_raw_cleanup_succeeds_and_is_redispatchable(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var session=await Sharing(f,ct);
            var member=session.Participants[0];
            var record=new LiveSoloRecording { Id=Guid.NewGuid(), MediaSessionId=session.Id, UserId=member.UserId,
                VideoTrackId=member.ScreenTrackId!, State=LiveSoloRecordingState.Failed, ReservedBytes=48L*1024*1024,EgressId="EG_failed",
                CreatedAt=f.Now, KeepUntil=f.Now.AddDays(30) };
            f.Db.LiveSoloRecordings.Add(record);await f.Db.SaveChangesAsync(ct);
            var files=Files();files.RemoveAsync(record.Id,ct).Returns(Task.FromException(new IOException("Spool unavailable")));
            var store=Store(f.Db,f,Substitute.For<ILiveSoloEgressGateway>(),files,new());
            await Assert.That(async()=>await store.RemoveRecordingRawAsync(record.Id,ct)).Throws<IOException>();
            await Assert.That((await f.Db.LiveSoloRecordings.AsNoTracking().SingleAsync(ct)).ReservedBytes).IsEqualTo(48L*1024*1024);
            var schedules=await new LiveSoloCaptureScheduleSource(f.Db).RebuildAsync(f.Now,ct);
            await Assert.That(schedules.Any(x=>x.Message is PruneLiveSoloCapture)).IsTrue();
            files.RemoveAsync(record.Id,ct).Returns(Task.CompletedTask);
            files.RemoveRecordingStagingAsync(record.EgressId!,ct).Returns(Task.FromException(new IOException("Staging unavailable")));
            await Assert.That(async()=>await store.RemoveRecordingRawAsync(record.Id,ct)).Throws<IOException>();
            await Assert.That((await f.Db.LiveSoloRecordings.AsNoTracking().SingleAsync(ct)).ReservedBytes).IsEqualTo(48L*1024*1024);
            files.RemoveRecordingStagingAsync(record.EgressId!,ct).Returns(Task.CompletedTask);
            await store.RemoveRecordingRawAsync(record.Id,ct);
            await Assert.That(record.ReservedBytes).IsEqualTo(0);await Assert.That(record.RawRemovedAt).IsNotNull();
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Failed);
        });
    }
    private static async Task<LiveSoloMediaSession> Sharing(LiveSoloMatchPersistenceTests.Fixture f,CancellationToken ct){var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.RecordingEnabled=true;
        foreach(var member in session.Participants){member.ScreenState=LiveSoloScreenState.Sharing;member.ScreenTrackId="track-"+member.Identity;}await f.Db.SaveChangesAsync(ct);return session;}
    private static LiveSoloCaptureStore Store(NoCtfDbContext db,LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloEgressGateway gateway,ILiveSoloCaptureFiles files,LiveKitMediaOptions options){var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
        return new(db,gateway,files,new ManagedFileUploads(new ManagedFileUploadRegistry(db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),options,f.Clock,messages,objects,new NoCTF.Infrastructure.Teams.Moderation.CompetitionModerationAuthorizer(db));}
}
