using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Realtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloRealtimePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Batch_access_is_scoped_to_current_competition_match_membership_and_collaborator_role(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var access = new LiveSoloRealtimeAccess(f.Db);
            LiveSoloRealtimeAccessRequest Request(string key, Guid user, LiveSoloRealtimeAudience audience) => new(key, user, f.Competition.Id, f.Match.Id, audience);
            var requests = new[] { Request("left", f.Left.Id, LiveSoloRealtimeAudience.Participant), Request("owner", f.Owner.Id, LiveSoloRealtimeAudience.Staff),
                Request("not-staff", f.Left.Id, LiveSoloRealtimeAudience.Staff), Request("unknown", Guid.NewGuid(), LiveSoloRealtimeAudience.Participant),
                Request("other-match", f.Left.Id, LiveSoloRealtimeAudience.Participant) with { MatchId = Guid.NewGuid() },
                Request("other-event", f.Owner.Id, LiveSoloRealtimeAudience.Staff) with { CompetitionId = Guid.NewGuid() } };
            await Assert.That(await access.EligibleAsync(requests, ct)).IsEquivalentTo(new[] { "left", "owner" });
            (await f.Db.Teams.SingleAsync(x => x.Id == f.LeftTeam.Id, ct)).IsBanned = true; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await access.EligibleAsync(requests, ct)).IsEquivalentTo(new[] { "owner" });
            var competition = await f.Db.Competitions.Include(x => x.Collaborators).SingleAsync(ct);
            competition.Collaborators.Add(new() { CompetitionId = f.Competition.Id, UserId = f.Right.Id, Role = CompetitionCollaboratorRole.Observer }); await f.Db.SaveChangesAsync(ct);
            var observer = new[] { Request("observer", f.Right.Id, LiveSoloRealtimeAudience.Staff) };
            await Assert.That(await access.EligibleAsync(observer, ct)).Contains("observer");
            competition.Collaborators.Clear(); await f.Db.SaveChangesAsync(ct);
            await Assert.That(await access.EligibleAsync(observer, ct)).IsEmpty();
            (await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).Enabled = false; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await access.EligibleAsync(requests, ct)).IsEmpty();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Intermediate_saves_are_captured_once_and_notification_flush_observes_committed_state(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var messages = Substitute.For<IPostCommitMessagePublisher>(); var committed = false;
            async Task Probe()
            {
                await using var read = new NoCtfDbContext(f.Options);
                committed = await read.Set<LiveSoloMatchSlot>().AnyAsync(x => x.MatchId == f.Match.Id && x.TeamId == f.LeftTeam.Id && x.ReadyConfirmedAt != null, ct);
            }
            messages.FlushCommittedMessagesAsync().Returns(_ => Probe());
            var store = new LiveSoloMatchStore(f.Db, new CompetitionModerationAuthorizer(f.Db), f.media, f.egress, Substitute.For<ILiveSoloRuntimePreparation>(), messages, clock: f.Clock);
            await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Left.Id, f.Match.ConcurrencyStamp, f.Now, ct);
            await messages.Received(1).PublishAsync(Arg.Is<LiveSoloMatchChanged>(x => x != null && x.CompetitionId == f.Competition.Id && x.MatchId == f.Match.Id));
            await Assert.That(committed).IsTrue();
            await using var failing = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(f.Options).AddInterceptors(new RejectCommit()).Options);
            var current = await failing.LiveSoloMatches.SingleAsync(ct); messages.ClearReceivedCalls();
            store = new(failing, new CompetitionModerationAuthorizer(failing), f.media, f.egress, Substitute.For<ILiveSoloRuntimePreparation>(), messages, clock: f.Clock);
            await Assert.That(async () => await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Right.Id, current.ConcurrencyStamp, f.Now, ct)).Throws<InvalidOperationException>();
            messages.Received(1).DiscardPendingMessages(); await messages.DidNotReceive().FlushCommittedMessagesAsync();
        });
    }
    [Test,Timeout(300_000)]
    public async Task Terminal_result_snapshot_is_queued_separately_from_private_realtime_invalidation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>
        {
            await using var f=await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);await f.PrepareAsync(ct);
            var messages=Substitute.For<IPostCommitMessagePublisher>();
            var store=new LiveSoloMatchStore(f.Db,new CompetitionModerationAuthorizer(f.Db),f.media,f.egress,Substitute.For<ILiveSoloRuntimePreparation>(),messages,clock:f.Clock);
            var match=await f.Db.LiveSoloMatches.SingleAsync(ct);
            var result=await store.ApplyAsync(new NoCTF.Application.LiveSolo.Adjudication.AdjudicateLiveSoloMatch(f.Competition.Id,f.Match.Id,f.Owner.Id,
                match.ConcurrencyStamp,match.CurrentRoundId,f.Round.ConcurrencyStamp,f.Round.TimelineRevision,LiveSoloJudgeAction.ForfeitMatch,f.RightTeam.Id,"terminal snapshot dispatch"),ct);
            await Assert.That(result.Failure).IsNull();
            await messages.Received(1).PublishAsync(Arg.Is<NoCTF.Application.LiveSolo.Media.SnapshotLiveSoloResult>(x=>x!=null&&x.MatchId==f.Match.Id));
            await messages.Received(1).PublishAsync(Arg.Is<LiveSoloMatchChanged>(x=>x!=null&&x.MatchId==f.Match.Id));
        });
    }
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected rollback");
    }
}
