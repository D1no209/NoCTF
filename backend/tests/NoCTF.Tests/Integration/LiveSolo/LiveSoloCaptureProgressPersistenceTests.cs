using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloCaptureProgressPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Active_but_stalled_program_alerts_once_denies_countdown_and_only_real_new_video_recovers_it(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.SingleAsync(ct);var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            program.StartedAt=f.Now.AddSeconds(-16);program.ImportedThrough=f.Now;program.NextSegmentSequence=1;
            program.LastFragmentImportedAt=f.Now.AddSeconds(-16);await f.Db.SaveChangesAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,ct).Returns([
                new(program.EgressId!,session.RoomIdentity,LiveSoloExportState.Active,program.StartedAt,null,null,[],program.Id)]);
            var files=Substitute.For<ILiveSoloCaptureFiles>();files.SegmentsAsync(program.Id,ct).Returns([]);var store=Store(f,gateway,files);
            await store.AdvanceAsync(session.Id,ct);await store.AdvanceAsync(session.Id,ct);
            await Assert.That(program.StalledAt).IsNotNull();
            var alert=await f.Db.Notifications.SingleAsync(x=>x.LiveSoloMediaAlertKind==LiveSoloMediaAlertKind.ProgramStalled,ct);
            await Assert.That(alert.TargetType).IsEqualTo(NotificationTargetType.CompetitionCollaborators);
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).State).IsEqualTo(LiveSoloMatchState.Preparing);
            var matches=f.Store(f.Db);var ready=await matches.ConfirmReadyAsync(f.Competition.Id,f.Match.Id,f.Left.Id,f.Match.ConcurrencyStamp,f.Now,ct);
            await matches.ConfirmReadyAsync(f.Competition.Id,f.Match.Id,f.Right.Id,ready.Match!.ConcurrencyStamp,f.Now,ct);
            await Assert.That((await matches.StartCountdownAsync(new(f.Competition.Id,f.Match.Id,f.Round.Id,f.Owner.Id,f.Round.ConcurrencyStamp,f.Now),ct)).Failure)
                .IsEqualTo(LiveSoloFailure.MediaUnavailable);
            files.SegmentsAsync(program.Id,ct).Returns([new(1,"program_00001.ts",TimeSpan.FromSeconds(2))]);
            files.OpenAsync(program.Id,"program_00001.ts",ct).Returns(_=>Task.FromResult<Stream?>(new MemoryStream([0x47,1,2,3])));
            await store.AdvanceAsync(session.Id,ct);await Assert.That(program.StalledAt).IsNull();
            await Assert.That(program.LastFragmentImportedAt).IsEqualTo(f.Now);
            f.Now=f.Now.AddSeconds(16);await store.AdvanceAsync(session.Id,ct);
            await Assert.That(await f.Db.Notifications.CountAsync(x=>x.LiveSoloMediaAlertKind==LiveSoloMediaAlertKind.ProgramStalled,ct)).IsEqualTo(2);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Recording_admission_failure_is_a_staff_notice_with_no_automatic_match_decision(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.RecordingEnabled=true;
            foreach(var member in session.Participants){member.ScreenState=LiveSoloScreenState.Sharing;member.ScreenTrackId="track-"+member.Identity;}
            await f.Db.SaveChangesAsync(ct);var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(x=>x.Id==session.CurrentProgramCaptureId,ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,ct).Returns([new(program.EgressId!,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],program.Id)]);
            var store=Store(f,gateway,Substitute.For<ILiveSoloCaptureFiles>(),new(){RecordingExportLimitBytes=16L*1024*1024,RecordingQuotaBytes=16L*1024*1024});
            await store.AdvanceAsync(session.Id,ct);await store.AdvanceAsync(session.Id,ct);
            await Assert.That(await f.Db.Notifications.CountAsync(x=>x.LiveSoloMediaAlertKind==LiveSoloMediaAlertKind.RecordingFailed,ct)).IsEqualTo(session.Participants.Count);
            await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloAdjudications.CountAsync(ct)).IsEqualTo(0);
        });
    }
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloEgressGateway gateway,ILiveSoloCaptureFiles files,LiveKitMediaOptions? options=null)
    {
        var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db,gateway,files,new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
            options??new(),f.Clock,messages,objects,new CompetitionModerationAuthorizer(f.Db));
    }
}
