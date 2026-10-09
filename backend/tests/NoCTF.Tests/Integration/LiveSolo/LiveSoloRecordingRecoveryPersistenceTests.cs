using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloRecordingRecoveryPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Capacity_denial_can_retry_without_an_RPC_but_uncertain_requests_cannot_be_started_again(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var record=await Record(f,ct);var gateway=Substitute.For<ILiveSoloEgressGateway>();var store=Store(f,gateway);
            var command=Command(f,record,LiveSoloRecordingAction.RetryPendingStart);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsNull();
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Pending);
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
            await Assert.That(await f.Db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(1);
            record.State=LiveSoloRecordingState.RequiresReview;record.RequestedAt=f.Now;record.ReservedBytes=32L*1024*1024;
            record.Failure=LiveSoloRecordingFailure.StartUncertain;await f.Db.SaveChangesAsync(ct);
            command=Command(f,record,LiveSoloRecordingAction.RetryPendingStart);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsEqualTo(LiveSoloFailure.NotReady);
            gateway.ListAsync(Arg.Any<string>(),ct).Returns([]);
            await Assert.That(await store.RecoverRecordingAsync(command with {Action=LiveSoloRecordingAction.ReconcileExport},ct)).IsEqualTo(LiveSoloFailure.NotReady);
            await Assert.That(record.ReservedBytes).IsEqualTo(32L*1024*1024);
            await Assert.That(await f.Db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(1);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Authoritative_reconciliation_never_starts_a_second_export_and_terminal_failure_can_create_a_fresh_chunk(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var record=await Record(f,ct);record.RequestedAt=f.Now;record.ReservedBytes=32L*1024*1024;
            await f.Db.SaveChangesAsync(ct);var room=(await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).RoomIdentity;
            var gateway=Substitute.For<ILiveSoloEgressGateway>();var observation=new LiveSoloExportObservation("real-job",room,LiveSoloExportState.Active,f.Now,null,null,[],record.Id);
            gateway.ListAsync(room,ct).Returns([observation]);var store=Store(f,gateway);
            await Assert.That(await store.RecoverRecordingAsync(Command(f,record,LiveSoloRecordingAction.ReconcileExport),ct)).IsNull();
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Recording);await Assert.That(record.EgressId).IsEqualTo("real-job");
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
            record.State=LiveSoloRecordingState.Failed;record.Failure=LiveSoloRecordingFailure.ExportFailed;record.ReservedBytes=0;record.RawRemovedAt=f.Now;
            await f.Db.SaveChangesAsync(ct);
            var command=Command(f,record,LiveSoloRecordingAction.StartNewChunk);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsEqualTo(LiveSoloFailure.NotReady);
            gateway.ListAsync(room,ct).Returns([observation with {State=LiveSoloExportState.Failed,EndedAt=f.Now}]);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsNull();
            var next=await f.Db.LiveSoloRecordings.SingleAsync(x=>x.Chunk==1,ct);
            await Assert.That(next.Id).IsNotEqualTo(record.Id);await Assert.That(next.State).IsEqualTo(LiveSoloRecordingState.Pending);
            await Assert.That((await f.Db.LiveSoloRecordingDecisions.SingleAsync(x=>x.Action==LiveSoloRecordingAction.StartNewChunk,ct)).ReplacementRecordingId).IsEqualTo(next.Id);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsEqualTo(LiveSoloFailure.Conflict);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Recovery_is_manager_only_and_stale_or_rolled_back_decisions_leave_no_work_or_audit(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var record=await Record(f,ct);var store=Store(f,Substitute.For<ILiveSoloEgressGateway>());var command=Command(f,record,LiveSoloRecordingAction.RetryPendingStart);
            await Assert.That(await store.RecoverRecordingAsync(command with {ActorId=f.Left.Id},ct)).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That(await store.RecoverRecordingAsync(command with {ExpectedStamp=Guid.NewGuid()},ct)).IsEqualTo(LiveSoloFailure.Conflict);
            EventHandler<SavingChangesEventArgs> fail=(_,_)=>throw new InvalidOperationException("rollback");
            f.Db.SavingChanges+=fail;
            await Assert.That(async()=>await store.RecoverRecordingAsync(command,ct)).Throws<InvalidOperationException>();
            f.Db.SavingChanges-=fail;f.Db.ChangeTracker.Clear();
            await Assert.That(await f.Db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await f.Db.LiveSoloRecordings.SingleAsync(ct)).State).IsEqualTo(LiveSoloRecordingState.RequiresReview);
        });
    }
    private static ChangeLiveSoloRecording Command(LiveSoloMatchPersistenceTests.Fixture f,LiveSoloRecording r,LiveSoloRecordingAction action)=>
        new(f.Competition.Id,f.Match.Id,r.Id,f.Owner.Id,r.ConcurrencyStamp,action,"reviewed capture recovery");
    [Test,Timeout(300_000)]
    public async Task Startup_timeout_persists_uncertainty_and_reservation_without_reissuing_the_export(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var record=await Record(f,ct);
            record.State=LiveSoloRecordingState.Pending;record.Failure=null;await f.Db.SaveChangesAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(Arg.Any<string>(),ct).Returns([]);
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(Task.FromException<LiveSoloExportObservation>(new TaskCanceledException("provider start timeout")));
            var store=Store(f,gateway);await store.AdvanceAsync(record.MediaSessionId,ct);await store.AdvanceAsync(record.MediaSessionId,ct);
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.RequiresReview);await Assert.That(record.Failure).IsEqualTo(LiveSoloRecordingFailure.StartUncertain);
            await Assert.That(record.ReservedBytes).IsEqualTo(32L*1024*1024);
            await gateway.Received(1).StartAsync(Arg.Is<LiveSoloExportRequest>(x=>x!=null&&x.Id==record.Id),ct);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Archive_recovery_requires_a_complete_matching_job_and_capacity_then_preserves_the_original_job_identity(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var record=await Record(f,ct);
            record.RequestedAt=f.Now;record.ReservedBytes=32L*1024*1024;record.EgressId="archive-job";record.Failure=LiveSoloRecordingFailure.ArchiveCapacityUnavailable;
            await f.Db.SaveChangesAsync(ct);var room=(await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).RoomIdentity;
            var gateway=Substitute.For<ILiveSoloEgressGateway>();var job=new LiveSoloExportObservation("archive-job",room,LiveSoloExportState.Complete,f.Now,f.Now,null,
                [new("/out/live-solo/"+record.Id.ToString("N")+"/recording.mp4",17L*1024*1024)],record.Id);
            gateway.ListAsync(room,ct).Returns([job]);var store=Store(f,gateway);var command=Command(f,record,LiveSoloRecordingAction.RetryArchive);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsEqualTo(LiveSoloFailure.NotReady);
            gateway.ListAsync(room,ct).Returns([job with {Files=[new("/out/live-solo/"+record.Id.ToString("N")+"/recording.mp4",4)]}]);
            await Assert.That(await store.RecoverRecordingAsync(command,ct)).IsNull();
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Finalizing);await Assert.That(record.EgressId).IsEqualTo("archive-job");
            await Assert.That(record.FileId).IsNull();await Assert.That(record.ReservedBytes).IsEqualTo(32L*1024*1024);
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Provider_failure_and_staff_notice_commit_together_and_rollback_remains_retryable(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var record=await Record(f,ct);
            record.State=LiveSoloRecordingState.Starting;record.RequestedAt=f.Now;record.ReservedBytes=32L*1024*1024;
            await f.Db.SaveChangesAsync(ct);var room=(await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).RoomIdentity;
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(room,ct).Returns([
                new("failed-job",room,LiveSoloExportState.Failed,null,f.Now,null,[],record.Id)]);
            var store=Store(f,gateway);
            EventHandler<SavingChangesEventArgs> fail=(_,_)=>{
                if(f.Db.ChangeTracker.Entries<LiveSoloRecording>().Any(x=>x.Entity.Id==record.Id&&x.Entity.State==LiveSoloRecordingState.Failed))
                    throw new InvalidOperationException("terminal transaction rollback");};
            f.Db.SavingChanges+=fail;
            await Assert.That(async()=>await store.AdvanceAsync(record.MediaSessionId,ct)).Throws<InvalidOperationException>();
            f.Db.SavingChanges-=fail;f.Db.ChangeTracker.Clear();
            var persisted=await f.Db.LiveSoloRecordings.SingleAsync(x=>x.Id==record.Id,ct);
            await Assert.That(persisted.State).IsEqualTo(LiveSoloRecordingState.Starting);
            await Assert.That(persisted.EgressId).IsNull();await Assert.That(persisted.ReservedBytes).IsEqualTo(32L*1024*1024);
            await Assert.That(await f.Db.Notifications.CountAsync(x=>x.SourceId==record.Id,ct)).IsEqualTo(0);
            await store.AdvanceAsync(record.MediaSessionId,ct);await store.AdvanceAsync(record.MediaSessionId,ct);
            await Assert.That(persisted.State).IsEqualTo(LiveSoloRecordingState.Failed);
            await Assert.That(persisted.Failure).IsEqualTo(LiveSoloRecordingFailure.ExportFailed);
            await Assert.That(persisted.EgressId).IsEqualTo("failed-job");
            await Assert.That(persisted.RawRemovedAt).IsNotNull();await Assert.That(persisted.ReservedBytes).IsEqualTo(0);
            await Assert.That(await f.Db.Notifications.CountAsync(x=>x.SourceId==record.Id,ct)).IsEqualTo(1);
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Lost_source_before_start_requires_review_and_recovery_needs_the_same_live_track(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);var record=await Record(f,ct);
            var member=await f.Db.Set<LiveSoloMediaParticipant>().SingleAsync(x=>x.MediaSessionId==record.MediaSessionId&&x.UserId==record.UserId,ct);
            member.ScreenState=LiveSoloScreenState.Disconnected;record.State=LiveSoloRecordingState.Pending;record.Failure=null;
            await f.Db.SaveChangesAsync(ct);var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(Arg.Any<string>(),ct).Returns([]);
            var store=Store(f,gateway);await store.AdvanceAsync(record.MediaSessionId,ct);await store.AdvanceAsync(record.MediaSessionId,ct);
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.RequiresReview);
            await Assert.That(record.Failure).IsEqualTo(LiveSoloRecordingFailure.SourceUnavailable);
            await Assert.That(record.RequestedAt).IsNull();await Assert.That(record.ReservedBytes).IsEqualTo(0);
            await Assert.That(await f.Db.Notifications.CountAsync(x=>x.SourceId==record.Id,ct)).IsEqualTo(1);
            await Assert.That(await store.RecoverRecordingAsync(Command(f,record,LiveSoloRecordingAction.RetryPendingStart),ct)).IsEqualTo(LiveSoloFailure.NotReady);
            member.ScreenState=LiveSoloScreenState.Sharing;member.ScreenTrackId="different-track";await f.Db.SaveChangesAsync(ct);
            await Assert.That(await store.RecoverRecordingAsync(Command(f,record,LiveSoloRecordingAction.RetryPendingStart),ct)).IsEqualTo(LiveSoloFailure.NotReady);
            member.ScreenTrackId=record.VideoTrackId;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await store.RecoverRecordingAsync(Command(f,record,LiveSoloRecordingAction.RetryPendingStart),ct)).IsNull();
            await Assert.That(record.State).IsEqualTo(LiveSoloRecordingState.Pending);
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
        });
    }
    private static async Task<LiveSoloRecording> Record(LiveSoloMatchPersistenceTests.Fixture f,CancellationToken ct)
    {
        var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.RecordingEnabled=true;
        var member=session.Participants[0];member.ScreenState=LiveSoloScreenState.Sharing;member.ScreenTrackId="track";
        var record=new LiveSoloRecording {Id=Guid.NewGuid(),MediaSessionId=session.Id,UserId=member.UserId,VideoTrackId="track",
            State=LiveSoloRecordingState.RequiresReview,Failure=LiveSoloRecordingFailure.CapacityUnavailable,CreatedAt=f.Now,KeepUntil=f.Now.AddDays(30)};
        f.Db.Add(record);await f.Db.SaveChangesAsync(ct);return record;
    }
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloEgressGateway gateway)
    {
        var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db,gateway,Substitute.For<ILiveSoloCaptureFiles>(),new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
            new() {RecordingExportLimitBytes=16L*1024*1024,RecordingQuotaBytes=128L*1024*1024},f.Clock,messages,objects,new CompetitionModerationAuthorizer(f.Db));
    }
}
