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
public sealed class LiveSoloProgramCursorPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Deleted_raw_segments_do_not_shift_later_video_timing_or_reimport_pruned_segments(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.CurrentProgramCaptureId=null;
            foreach(var member in session.Participants)member.ScreenState=LiveSoloScreenState.Sharing;await f.Db.SaveChangesAsync(ct);
            var files=Substitute.For<ILiveSoloCaptureFiles>();var gateway=Substitute.For<ILiveSoloEgressGateway>();
            var start=f.Now;LiveSoloExportObservation? job=null;gateway.ListAsync(session.RoomIdentity,ct).Returns(_=>job is null?[]:new[]{job});
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(call=>{
                var request=call.Arg<LiveSoloExportRequest>()!;job=new("cursor-job",session.RoomIdentity,LiveSoloExportState.Active,start,null,null,[],request.Id);return job;});
            files.SegmentsAsync(Arg.Any<Guid>(),ct).Returns([new(0,"program_00000.ts",TimeSpan.FromSeconds(2))]);
            files.OpenAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(_=>Task.FromResult<Stream?>(new MemoryStream([0x47,1,2,3])));
            var store=Store(f,files,gateway);await store.AdvanceAsync(session.Id,ct);
            var capture=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            await Assert.That(capture.NextSegmentSequence).IsEqualTo(1);
            await files.Received(1).RemoveSegmentAsync(capture.Id,"program_00000.ts",ct);
            f.Now=f.Now.AddMinutes(7);await store.PruneAsync(session.Id,ct);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(x=>x.ProgramCaptureId==capture.Id,ct)).IsEqualTo(0);
            files.SegmentsAsync(capture.Id,ct).Returns([new(1,"program_00001.ts",TimeSpan.FromSeconds(3))]);
            await store.AdvanceAsync(session.Id,ct);
            var next=await f.Db.LiveSoloProgramSegments.SingleAsync(x=>x.ProgramCaptureId==capture.Id,ct);
            await Assert.That(next.StartedAt).IsEqualTo(start.AddSeconds(2));await Assert.That(next.EndedAt).IsEqualTo(start.AddSeconds(5));
            await Assert.That(next.PublicAt).IsGreaterThanOrEqualTo(f.Now.AddSeconds(session.PublicDelaySeconds));
            await Assert.That(capture.NextSegmentSequence).IsEqualTo(2);
            files.SegmentsAsync(capture.Id,ct).Returns([new(0,"program_00000.ts",TimeSpan.FromSeconds(2)),new(1,"program_00001.ts",TimeSpan.FromSeconds(3))]);
            await store.AdvanceAsync(session.Id,ct);
            await Assert.That(await f.Db.LiveSoloProgramSegments.CountAsync(x=>x.ProgramCaptureId==capture.Id,ct)).IsEqualTo(1);
            await files.Received(1).OpenAsync(capture.Id,"program_00000.ts",ct);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Cleanup_failure_retains_the_committed_cursor_and_retries_without_uploading_again_while_gaps_fail_closed(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.CurrentProgramCaptureId=null;
            foreach(var member in session.Participants)member.ScreenState=LiveSoloScreenState.Sharing;await f.Db.SaveChangesAsync(ct);
            var files=Substitute.For<ILiveSoloCaptureFiles>();var gateway=Substitute.For<ILiveSoloEgressGateway>();LiveSoloExportObservation? job=null;
            gateway.ListAsync(session.RoomIdentity,ct).Returns(_=>job is null?[]:new[]{job});
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(call=>{var request=call.Arg<LiveSoloExportRequest>()!;
                job=new("cleanup-job",session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],request.Id);return job;});
            files.SegmentsAsync(Arg.Any<Guid>(),ct).Returns([new(0,"program_00000.ts",TimeSpan.FromSeconds(2))]);
            files.OpenAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(_=>Task.FromResult<Stream?>(new MemoryStream([0x47,1,2,3])));
            files.RemoveSegmentAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(Task.FromException(new IOException("cleanup unavailable")));
            var store=Store(f,files,gateway);await Assert.That(async()=>await store.AdvanceAsync(session.Id,ct)).Throws<IOException>();
            var capture=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            await Assert.That(capture.NextSegmentSequence).IsEqualTo(1);
            files.RemoveSegmentAsync(Arg.Any<Guid>(),Arg.Any<string>(),ct).Returns(Task.CompletedTask);
            await store.AdvanceAsync(session.Id,ct);await files.Received(1).OpenAsync(capture.Id,"program_00000.ts",ct);
            files.SegmentsAsync(capture.Id,ct).Returns([new(2,"program_00002.ts",TimeSpan.FromSeconds(2))]);
            await Assert.That(async()=>await store.AdvanceAsync(session.Id,ct)).Throws<InvalidDataException>();
            await Assert.That(capture.NextSegmentSequence).IsEqualTo(1);
        });
    }
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloCaptureFiles files,ILiveSoloEgressGateway gateway)
    {
        var objects=Substitute.For<IStore>();var publisher=Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db,gateway,files,new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,publisher,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
            new(),f.Clock,publisher,objects,new CompetitionModerationAuthorizer(f.Db));
    }
}
