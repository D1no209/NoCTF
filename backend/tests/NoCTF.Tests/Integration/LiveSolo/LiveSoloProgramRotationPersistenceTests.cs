using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloProgramRotationPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Rotation_stops_the_existing_job_and_awaits_complete_import_before_installing_one_new_UUID(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.CurrentProgramCaptureId=null;
            foreach(var member in session.Participants)member.ScreenState=LiveSoloScreenState.Sharing;await f.Db.SaveChangesAsync(ct);
            var files=Substitute.For<ILiveSoloCaptureFiles>();var gateway=Substitute.For<ILiveSoloEgressGateway>();var start=f.Now;
            LiveSoloExportObservation? job=null;gateway.ListAsync(session.RoomIdentity,ct).Returns(_=>job is null?[]:new[]{job});
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(call=>{var req=call.Arg<LiveSoloExportRequest>()!;
                job=new("rotation-"+req.Id,session.RoomIdentity,LiveSoloExportState.Active,start,null,null,[],req.Id);return job;});
            files.SegmentsAsync(Arg.Any<Guid>(),ct).Returns([new(0,"program_00000.ts",TimeSpan.FromSeconds(2))]);
            files.OpenAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(_=>Task.FromResult<Stream?>(new MemoryStream([0x47,1,2,3])));
            var store=Store(f,files,gateway);await store.AdvanceAsync(session.Id,ct);
            var capture=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            var initialCount=await f.Db.LiveSoloProgramCaptures.CountAsync(x=>x.MediaSessionId==session.Id,ct);
            var oldId=capture.Id;f.Now=f.Now.AddSeconds(30);await store.AdvanceAsync(session.Id,ct);
            await Assert.That(capture.RotationRequested).IsTrue();await Assert.That(capture.State).IsEqualTo(LiveSoloCaptureState.Stopping);
            await gateway.Received(1).StopAsync(job!.Id,ct);await Assert.That(session.CurrentProgramCaptureId).IsEqualTo(oldId);
            job=job with {State=LiveSoloExportState.Complete,EndedAt=f.Now};await store.AdvanceAsync(session.Id,ct);
            await Assert.That(capture.ImportedAt).IsNotNull();await Assert.That(session.CurrentProgramCaptureId).IsNotEqualTo(oldId);
            await Assert.That(await f.Db.LiveSoloProgramCaptures.CountAsync(x=>x.MediaSessionId==session.Id,ct)).IsEqualTo(initialCount+1);
            var next=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            await Assert.That(next.State).IsEqualTo(LiveSoloCaptureState.Pending);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(x=>x.ProgramCaptureId==oldId,ct)).IsEqualTo(1);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Stop_failure_keeps_the_rotation_pending_and_an_import_gap_prevents_replacement(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);
            var capture=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            capture.StartedAt=f.Now.AddSeconds(-31);capture.NextSegmentSequence=1;capture.ImportedThrough=f.Now.AddSeconds(-29);
            await f.Db.SaveChangesAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();var job=new LiveSoloExportObservation(capture.EgressId!,session.RoomIdentity,
                LiveSoloExportState.Active,capture.StartedAt,null,null,[],capture.Id);
            gateway.ListAsync(session.RoomIdentity,ct).Returns([job]);gateway.StopAsync(job.Id,ct).Returns(Task.FromException(new HttpRequestException("stop unavailable")));
            var files=Substitute.For<ILiveSoloCaptureFiles>();files.SegmentsAsync(capture.Id,ct).Returns([]);var store=Store(f,files,gateway);
            await Assert.That(async()=>await store.AdvanceAsync(session.Id,ct)).Throws<HttpRequestException>();
            await Assert.That(capture.RotationRequested).IsTrue();await Assert.That(session.CurrentProgramCaptureId).IsEqualTo(capture.Id);
            gateway.ListAsync(session.RoomIdentity,ct).Returns([job with {State=LiveSoloExportState.Complete,EndedAt=f.Now}]);
            files.SegmentsAsync(capture.Id,ct).Returns([new(2,"program_00002.ts",TimeSpan.FromSeconds(2))]);
            await Assert.That(async()=>await store.AdvanceAsync(session.Id,ct)).Throws<InvalidDataException>();
            await Assert.That(session.CurrentProgramCaptureId).IsEqualTo(capture.Id);
        });
    }
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloCaptureFiles files,ILiveSoloEgressGateway gateway)
    {
        var objects=Substitute.For<IStore>();var publisher=Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db,gateway,files,new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,publisher,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
            new() {ProgramChunkSeconds=30},f.Clock,publisher,objects,new CompetitionModerationAuthorizer(f.Db));
    }
}
