using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloViewerPersistenceTests
{
    [Test,Timeout(300_000)]
    public async Task Concurrent_admission_uses_one_shared_capacity_and_expiry_or_leave_releases_it(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            (await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).MaximumViewers=1;await f.Db.SaveChangesAsync(ct);
            async Task<LiveSoloViewerResult> Enter(){await using var db=new NoCtfDbContext(f.Options);return await Store(db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct);}
            var results=await Task.WhenAll(Enter(),Enter());
            await Assert.That(results.Count(x=>x.Admission is not null)).IsEqualTo(1);
            await Assert.That(results.Count(x=>x.Failure==LiveSoloViewerFailure.CapacityReached)).IsEqualTo(1);
            var lease=results.Single(x=>x.Admission is not null).Admission!;
            var second=new LiveSoloMatch {Id=Guid.NewGuid(),CompetitionId=f.Competition.Id,Stage=99,Position=1,StartedAt=f.Now,State=LiveSoloMatchState.Running,RequiredWins=2};
            f.Db.Add(second);await f.Db.SaveChangesAsync(ct);
            await Assert.That((await Store(f.Db,f).EnterAsync(f.Competition.Id,second.Id,Guid.Empty,null,ct)).Failure).IsEqualTo(LiveSoloViewerFailure.CapacityReached);
            await Assert.That((await Store(f.Db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,lease.Id,ct)).Admission!.Id).IsEqualTo(lease.Id);
            f.Now=f.Now.AddSeconds(15);
            await Assert.That((await Store(f.Db,f).RenewAsync(f.Competition.Id,f.Match.Id,Guid.Empty,lease.Id,ct)).Failure).IsEqualTo(LiveSoloViewerFailure.Expired);
            var next=(await Store(f.Db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!;
            await Assert.That(next.Id).IsNotEqualTo(lease.Id);await Assert.That(await f.Db.Set<LiveSoloViewerLease>().CountAsync(ct)).IsEqualTo(1);
            await Store(f.Db,f).LeaveAsync(f.Competition.Id,f.Match.Id,Guid.Empty,next.Id,ct);
            await Assert.That((await Enter()).Admission).IsNotNull();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Lease_binds_match_and_identity_and_policy_changes_revoke_existing_reads(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var segment=await f.Db.LiveSoloProgramSegments.SingleAsync(ct);f.Now=segment.PublicAt;
            var viewers=Store(f.Db,f);var lease=(await viewers.EnterAsync(f.Competition.Id,f.Match.Id,f.Owner.Id,null,ct)).Admission!;
            var anonymous=(await viewers.EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!;
            var reader=new LiveSoloProgramReader(f.Db,new CompetitionModerationAuthorizer(f.Db),Substitute.For<IStore>(),f.Clock);
            await Assert.That(await reader.ReadAsync(f.Competition.Id,f.Match.Id,f.Owner.Id,lease.Id,ct)).IsNotNull();
            await Assert.That(await reader.ReadAsync(f.Competition.Id,f.Match.Id,Guid.Empty,lease.Id,ct)).IsNull();
            await Assert.That(await reader.ReadAsync(f.Competition.Id,Guid.NewGuid(),f.Owner.Id,lease.Id,ct)).IsNull();
            await viewers.LeaveAsync(f.Competition.Id,f.Match.Id,f.Left.Id,lease.Id,ct);
            await Assert.That((await viewers.RenewAsync(f.Competition.Id,f.Match.Id,f.Owner.Id,lease.Id,ct)).Admission).IsNotNull();
            (await f.Db.Competitions.SingleAsync(ct)).AccessMode=CompetitionAccessMode.StaffOnly;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(f.Competition.Id,f.Match.Id,Guid.Empty,anonymous.Id,ct)).IsNull();
            await Assert.That((await viewers.RenewAsync(f.Competition.Id,f.Match.Id,Guid.Empty,anonymous.Id,ct)).Failure).IsEqualTo(LiveSoloViewerFailure.Unavailable);
            (await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).Enabled=false;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(f.Competition.Id,f.Match.Id,f.Owner.Id,lease.Id,ct)).IsNull();
        });
    }
    [Test,Timeout(300_000)]
    public async Task An_opened_stream_is_closed_if_lease_expires_during_storage_open(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var segment=await f.Db.LiveSoloProgramSegments.SingleAsync(ct);f.Now=segment.PublicAt;
            var lease=(await Store(f.Db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!;
            var objects=Substitute.For<IStore>();objects.ObjectExists(Arg.Any<string>(),ct).Returns(true);var stream=new MemoryStream([1,2,3]);
            objects.OpenRead(Arg.Any<string>(),ct).Returns(_=>{f.Now=f.Now.AddSeconds(15);return Task.FromResult<Stream?>(stream);});
            var reader=new LiveSoloProgramReader(f.Db,new CompetitionModerationAuthorizer(f.Db),objects,f.Clock);
            await Assert.That(await reader.OpenSegmentAsync(f.Competition.Id,f.Match.Id,segment.Id,Guid.Empty,lease.Id,ct)).IsNull();
            await Assert.That(stream.CanRead).IsFalse();
        });
    }
    private static LiveSoloViewerStore Store(NoCtfDbContext db,LiveSoloMatchPersistenceTests.Fixture f)=>new(db,new CompetitionModerationAuthorizer(db),f.Clock);
    [Test,Timeout(300_000)]
    public async Task Fifty_independent_renewals_do_not_contend_on_the_global_admission_budget_or_create_extra_slots(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            var leases=new List<Guid>();
            for(var index=0;index<50;index++)leases.Add((await Store(f.Db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!.Id);
            async Task<NoCTF.Application.LiveSolo.Media.LiveSoloViewerResult> Renew(Guid id){await using var db=new NoCtfDbContext(f.Options);return await Store(db,f).RenewAsync(f.Competition.Id,f.Match.Id,Guid.Empty,id,ct);}
            var results=await Task.WhenAll(leases.Select(Renew));
            await Assert.That(results.All(x=>x.Failure is null)).IsTrue();
            await Assert.That(await f.Db.Set<LiveSoloViewerLease>().CountAsync(ct)).IsEqualTo(50);
            await Assert.That((await Store(f.Db,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Failure)
                .IsEqualTo(NoCTF.Application.LiveSolo.Media.LiveSoloViewerFailure.CapacityReached);
        });
    }
    [Test,Timeout(300_000)]
    public async Task Lowering_capacity_requalifies_existing_leases_and_transaction_failure_cannot_create_a_slot(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);await f.StartAsync(ct);
            f.Now=(await f.Db.LiveSoloProgramSegments.SingleAsync(ct)).PublicAt;
            await using(var failing=new NoCtfDbContext(f.Options))
            {
                failing.SavingChanges+=(_,_)=>throw new InvalidOperationException("forced rollback");
                await Assert.That(async()=>await Store(failing,f).EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Throws<InvalidOperationException>();
            }
            await Assert.That(await f.Db.Set<LiveSoloViewerLease>().CountAsync(ct)).IsEqualTo(0);
            var viewers=Store(f.Db,f);var first=(await viewers.EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!;
            var second=(await viewers.EnterAsync(f.Competition.Id,f.Match.Id,Guid.Empty,null,ct)).Admission!;
            (await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).MaximumViewers=1;await f.Db.SaveChangesAsync(ct);
            var reader=new LiveSoloProgramReader(f.Db,new CompetitionModerationAuthorizer(f.Db),Substitute.For<IStore>(),f.Clock);
            var permitted=await reader.ReadAsync(f.Competition.Id,f.Match.Id,Guid.Empty,first.Id,ct) is not null;
            await Assert.That(await reader.ReadAsync(f.Competition.Id,f.Match.Id,Guid.Empty,second.Id,ct) is not null).IsEqualTo(!permitted);
            var rejected=permitted?second:first;
            await Assert.That((await viewers.RenewAsync(f.Competition.Id,f.Match.Id,Guid.Empty,rejected.Id,ct)).Failure).IsEqualTo(LiveSoloViewerFailure.CapacityReached);
        });
    }
}
