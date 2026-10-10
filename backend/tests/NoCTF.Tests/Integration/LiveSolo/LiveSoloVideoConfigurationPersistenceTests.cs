using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloVideoConfigurationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Incremental_migration_preserves_existing_platform_configuration_and_adds_video_defaults(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261009212739_LiveSoloDelayedResults", ct);
            // Select only pre-existing columns while the database is still on the preceding migration.
            await db.PlatformSettings.Where(x => x.Id == 1).ExecuteUpdateAsync(update => update.SetProperty(x => x.Name, "Existing platform"), ct);
            await db.Database.MigrateAsync(ct);
            db.ChangeTracker.Clear();
            var configuration = await new PlatformConfigurationStore(db).GetAsync(ct);
            await Assert.That(configuration.Name).IsEqualTo("Existing platform");
            await Assert.That(configuration.LiveSoloVideo).IsEqualTo(LiveSoloVideoPolicy.Default);
            await Assert.That(await db.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Platform_video_policy_persists_defaults_revision_and_rollback_without_changing_other_settings(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);var store=new PlatformConfigurationStore(f.Db);
            var initial=await store.GetAsync(ct);await Assert.That(initial.LiveSoloVideo!.MaximumWidth).IsEqualTo(1280);
            await using(var transaction=await f.Db.Database.BeginTransactionAsync(ct))
            {await store.UpdateLiveSoloVideoAsync(640,360,5,500_000,f.Now,ct);await transaction.RollbackAsync(ct);}
            f.Db.ChangeTracker.Clear();var rolledBack=await store.GetAsync(ct);
            await Assert.That(rolledBack.LiveSoloVideo!.PolicyStamp).IsEqualTo(initial.LiveSoloVideo.PolicyStamp);
            var updated=await store.UpdateLiveSoloVideoAsync(640,360,5,500_000,f.Now,ct);
            await Assert.That(updated.LiveSoloVideo!.PolicyStamp).IsNotEqualTo(initial.LiveSoloVideo.PolicyStamp);
            var unchanged=await store.UpdateLiveSoloVideoAsync(640,360,5,500_000,f.Now,ct);
            await Assert.That(unchanged.LiveSoloVideo!.PolicyStamp).IsEqualTo(updated.LiveSoloVideo.PolicyStamp);
            await store.UpdateExperimentalFeaturesAsync(true,f.Now,ct);
            var after=await store.GetAsync(ct);await Assert.That(after.LiveSoloVideo!.MaximumBitrateBitsPerSecond).IsEqualTo(500_000);
            await Assert.That(after.Name).IsEqualTo(initial.Name);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Started_exports_keep_effective_policy_and_byte_reservations_while_new_tasks_use_current_platform_limits(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var session=await f.Db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(ct);session.RecordingEnabled=true;
            foreach(var member in session.Participants){member.ScreenState=LiveSoloScreenState.Sharing;member.ScreenTrackId="track-"+member.Identity;}
            var old=await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);old.State=LiveSoloCaptureState.Failed;
            var programme=new LiveSoloProgramCapture {Id=Guid.NewGuid(),MediaSessionId=session.Id,CreatedAt=f.Now};f.Db.Add(programme);await f.Db.SaveChangesAsync(ct);session.CurrentProgramCaptureId=programme.Id;await f.Db.SaveChangesAsync(ct);
            var settings=new PlatformConfigurationStore(f.Db);var policy=(await settings.UpdateLiveSoloVideoAsync(640,360,5,500_000,f.Now,ct)).LiveSoloVideo!;
            var jobs=new List<LiveSoloExportObservation>();var gateway=Substitute.For<ILiveSoloEgressGateway>();gateway.ListAsync(session.RoomIdentity,ct).Returns(_=>jobs.ToArray());
            gateway.StartAsync(Arg.Any<LiveSoloExportRequest>(),ct).Returns(call=>{var request=call.Arg<LiveSoloExportRequest>()!;
                var job=new LiveSoloExportObservation("EG_"+request.Id.ToString("N"),session.RoomIdentity,LiveSoloExportState.Active,f.Now,null,null,[],request.Id);jobs.Add(job);return job;});
            var files=Substitute.For<ILiveSoloCaptureFiles>();files.SegmentsAsync(Arg.Any<Guid>(),ct).Returns([]);
            var objects=Substitute.For<IStore>();var messages=Substitute.For<IPostCommitMessagePublisher>();
            var captures=new LiveSoloCaptureStore(f.Db,gateway,files,new ManagedFileUploads(new ManagedFileUploadRegistry(f.Db,messages,NullLogger<ManagedFileUploadRegistry>.Instance),objects),
                new(){RecordingExportLimitBytes=16L*1024*1024,RecordingQuotaBytes=128L*1024*1024},f.Clock,messages,objects,new CompetitionModerationAuthorizer(f.Db));
            await captures.AdvanceAsync(session.Id,ct);var recordings=await f.Db.LiveSoloRecordings.AsNoTracking().ToArrayAsync(ct);
            await Assert.That(programme.VideoPolicyStamp).IsEqualTo(policy.PolicyStamp);await Assert.That(programme.VideoMaximumWidth).IsEqualTo(640);
            await Assert.That(recordings.All(x=>x.VideoBitrateBitsPerSecond==500_000&&x.ReservedBytes==48L*1024*1024)).IsTrue();
            await gateway.Received(3).StartAsync(Arg.Is<LiveSoloExportRequest>(x=>x!=null&&x.VideoPolicy.PolicyStamp==policy.PolicyStamp),ct);
            await settings.UpdateLiveSoloVideoAsync(320,180,2,256_000,f.Now,ct);await captures.AdvanceAsync(session.Id,ct);
            await Assert.That(programme.VideoBitrateBitsPerSecond).IsEqualTo(500_000);
            await Assert.That((await f.Db.LiveSoloRecordings.AsNoTracking().ToArrayAsync(ct)).All(x=>x.VideoBitrateBitsPerSecond==500_000&&x.ReservedBytes==48L*1024*1024)).IsTrue();
            await gateway.Received(3).StartAsync(Arg.Any<LiveSoloExportRequest>(),ct);
        });
    }
}
