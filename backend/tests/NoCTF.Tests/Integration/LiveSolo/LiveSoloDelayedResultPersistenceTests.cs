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
public sealed class LiveSoloDelayedResultPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Result_is_delayed_immutable_and_separate_from_the_last_video_frame_and_survives_video_window_cleanup(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);program.NextSegmentSequence=1;
            (await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).PublicDelaySeconds=60;
            var segment=await f.Db.LiveSoloProgramSegments.SingleAsync(ct);f.Now=segment.PublicAt;
            var match=await f.Db.LiveSoloMatches.SingleAsync(ct);match.State=LiveSoloMatchState.Completed;match.LeftWins=1;match.WinnerTeamId=f.LeftTeam.Id;
            await f.Db.SaveChangesAsync(ct);var store=Store(f.Db,f);await store.SnapshotResultAsync(match.Id,ct);await store.SnapshotResultAsync(match.Id,ct);
            var frame=await f.Db.LiveSoloProgramFrames.SingleAsync(x=>x.Kind==LiveSoloProgramFrameKind.DelayedResult,ct);
            await Assert.That(frame.PublicAt).IsEqualTo(f.Now.AddSeconds(60));
            var lease=(await new LiveSoloViewerStore(f.Db,new CompetitionModerationAuthorizer(f.Db),f.Clock).EnterAsync(f.Competition.Id,match.Id,Guid.Empty,null,ct)).Admission!;
            var reader=new LiveSoloProgramReader(f.Db,new CompetitionModerationAuthorizer(f.Db),Substitute.For<IStore>(),f.Clock);
            await Assert.That((await reader.ReadAsync(f.Competition.Id,match.Id,Guid.Empty,lease.Id,ct))!.Result).IsNull();
            f.Now=frame.PublicAt!.Value;
            lease=(await new LiveSoloViewerStore(f.Db,new CompetitionModerationAuthorizer(f.Db),f.Clock).EnterAsync(f.Competition.Id,match.Id,Guid.Empty,null,ct)).Admission!;
            var published=(await reader.ReadAsync(f.Competition.Id,match.Id,Guid.Empty,lease.Id,ct))!;
            await Assert.That(published.Result!.LeftWins).IsEqualTo(1);
            await Assert.That(published.Segments.Single().State.LeftWins).IsEqualTo(0);
            f.Now=segment.RemoveAfter;await store.PruneAsync(segment.MediaSessionId,ct);
            lease=(await new LiveSoloViewerStore(f.Db,new CompetitionModerationAuthorizer(f.Db),f.Clock).EnterAsync(f.Competition.Id,match.Id,Guid.Empty,null,ct)).Admission!;
            var noVideo=(await reader.ReadAsync(f.Competition.Id,match.Id,Guid.Empty,lease.Id,ct))!;
            await Assert.That(noVideo.Segments.Count).IsEqualTo(0);await Assert.That(noVideo.Result!.WinnerTeamId).IsEqualTo(f.LeftTeam.Id);
            frame.LeftWins=9;await Assert.That(async()=>await f.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Duplicate_snapshot_work_is_idempotent_and_later_corrections_wait_for_their_own_publication_time(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            (await f.Db.LiveSoloProgramCaptures.SingleAsync(ct)).NextSegmentSequence=1;
            (await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).PublicDelaySeconds=60;
            var match=await f.Db.LiveSoloMatches.SingleAsync(ct);match.State=LiveSoloMatchState.Completed;match.LeftWins=1;match.WinnerTeamId=f.LeftTeam.Id;await f.Db.SaveChangesAsync(ct);
            async Task Snapshot(){await using var db=new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options);await Store(db,f).SnapshotResultAsync(match.Id,ct);}
            await Task.WhenAll(Snapshot(), Snapshot(), Snapshot(), Snapshot());
            await Assert.That(await f.Db.LiveSoloProgramFrames.CountAsync(x=>x.Kind==LiveSoloProgramFrameKind.DelayedResult,ct)).IsEqualTo(1);
            f.Now=f.Now.AddSeconds(70);match.LeftWins=0;match.RightWins=1;match.WinnerTeamId=f.RightTeam.Id;await f.Db.SaveChangesAsync(ct);
            await Snapshot();var rows=await f.Db.LiveSoloProgramFrames.Where(x=>x.Kind==LiveSoloProgramFrameKind.DelayedResult).OrderBy(x=>x.OccurredAt).ToArrayAsync(ct);
            await Assert.That(rows.Count()).IsEqualTo(2);await Assert.That(rows[0].LeftWins).IsEqualTo(1);await Assert.That(rows[1].PublicAt).IsEqualTo(f.Now.AddSeconds(60));
        });
    }
    private static LiveSoloCaptureStore Store(NoCTF.Infrastructure.Persistence.NoCtfDbContext db,LiveSoloMatchPersistenceTests.Fixture f)
    {
        var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
        return new(db,Substitute.For<ILiveSoloEgressGateway>(),Substitute.For<ILiveSoloCaptureFiles>(),
            new ManagedFileUploads(new ManagedFileUploadRegistry(db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),new(),f.Clock,messages,objects,
            new CompetitionModerationAuthorizer(db));
    }
}
