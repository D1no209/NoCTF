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
public sealed class LiveSoloProgramRecoveryPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Unknown_start_only_reconciles_a_matching_existing_job_and_cannot_create_another_capture(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.SingleAsync(ct);var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            program.State=LiveSoloCaptureState.RequiresReview;program.EgressId=null;program.RequestedAt=f.Now;await f.Db.SaveChangesAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,ct).Returns([]);var store=Store(f,gateway);
            var command=Command(f,program,LiveSoloProgramAction.ReconcileExport);
            await Assert.That((await store.RecoverAsync(command,ct)).Failure).IsEqualTo(LiveSoloFailure.NotReady);
            gateway.ListAsync(session.RoomIdentity,ct).Returns([new("found-job",session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],program.Id)]);
            await Assert.That((await store.RecoverAsync(command,ct)).Failure).IsNull();await Assert.That(program.State).IsEqualTo(LiveSoloCaptureState.Active);
            await Assert.That(program.EgressId).IsEqualTo("found-job");await Assert.That(await f.Db.LiveSoloProgramCaptures.CountAsync(ct)).IsEqualTo(1);
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
            var decision=await f.Db.Set<LiveSoloProgramDecision>().SingleAsync(ct);decision.Reason="tamper";
            await Assert.That(async()=>await f.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Rotation_of_an_active_job_waits_for_stop_but_a_proven_terminal_job_can_install_a_fresh_capture(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.SingleAsync(ct);var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();var job=new LiveSoloExportObservation(program.EgressId!,session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],program.Id);
            gateway.ListAsync(session.RoomIdentity,ct).Returns([job]);var store=Store(f,gateway);
            var rotate=await store.RecoverAsync(Command(f,program,LiveSoloProgramAction.Rotate),ct);
            await Assert.That(rotate.Failure).IsNull();await Assert.That(program.RotationRequested).IsTrue();
            await Assert.That(session.CurrentProgramCaptureId).IsEqualTo(program.Id);
            gateway.ListAsync(session.RoomIdentity,ct).Returns([job with {State=LiveSoloExportState.Failed,EndedAt=f.Now}]);
            await Assert.That((await store.RecoverAsync(Command(f,program,LiveSoloProgramAction.Rotate),ct)).Failure).IsNull();
            await Assert.That(session.CurrentProgramCaptureId).IsNotEqualTo(program.Id);await Assert.That(await f.Db.LiveSoloProgramCaptures.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(program.RawCleanupAuthorizedAt).IsNotNull();await Assert.That(program.ImportedAt).IsNull();
            await gateway.DidNotReceiveWithAnyArgs().StartAsync(default!,default);
            await Assert.That((await store.HealthAsync(f.Competition.Id,f.Match.Id,f.Owner.Id,ct))!.State).IsEqualTo(LiveSoloCaptureState.Pending);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Recovery_permissions_versions_and_transaction_rollback_preserve_current_media_identity(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.SingleAsync(ct);var program=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,ct).Returns([new(program.EgressId!,session.RoomIdentity,LiveSoloExportState.Complete,f.Now,f.Now,null,[],program.Id)]);
            var store=Store(f,gateway);var command=Command(f,program,LiveSoloProgramAction.RetryImport);
            await Assert.That((await store.RecoverAsync(command with {ActorId=f.Left.Id},ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That(await store.HealthAsync(f.Competition.Id,f.Match.Id,f.Left.Id,ct)).IsNull();
            await Assert.That((await store.RecoverAsync(command with {ExpectedStamp=Guid.NewGuid()},ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
            EventHandler<SavingChangesEventArgs> fail=(_,_)=>throw new InvalidOperationException("rollback");
            f.Db.SavingChanges+=fail;await Assert.That(async()=>await store.RecoverAsync(command,ct)).Throws<InvalidOperationException>();
            f.Db.SavingChanges-=fail;f.Db.ChangeTracker.Clear();
            await Assert.That(await f.Db.Set<LiveSoloProgramDecision>().CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).CurrentProgramCaptureId).IsEqualTo(program.Id);
        });
    }
    private static RecoverLiveSoloProgram Command(LiveSoloMatchPersistenceTests.Fixture f,LiveSoloProgramCapture p,LiveSoloProgramAction action)=>
        new(f.Competition.Id,f.Match.Id,f.Owner.Id,p.Id,p.ConcurrencyStamp,action,"operator verified export state");
    private static LiveSoloCaptureStore Store(LiveSoloMatchPersistenceTests.Fixture f,ILiveSoloEgressGateway gateway)
    {
        var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
        return new(f.Db,gateway,Substitute.For<ILiveSoloCaptureFiles>(),new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
            new(),f.Clock,messages,objects,new CompetitionModerationAuthorizer(f.Db));
    }
}
