using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloGroupEditingPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Replacement_preserves_order_and_stale_or_active_edits_do_not_overwrite_the_group(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var store = f.Store(f.Db);
            var entries = f.Entries.Select(x => new LiveSoloQuestionGroupEntry(x.Id,null)).ToArray();
            var created = await store.SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(null,"Reserve",true,null,entries,null),f.Now,ct);
            await Assert.That(created.Failure).IsNull(); var first = created.Group!;
            var replacement = await store.SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(first.Id,"Reordered",true,25,entries.Reverse().ToArray(),first.ConcurrencyStamp),f.Now,ct);
            await Assert.That(replacement.Failure).IsNull();
            await Assert.That(replacement.Group!.Questions[0].CompetitionChallengeId).IsEqualTo(entries[1].CompetitionChallengeId);
            var stale = await store.SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(first.Id,"Stale",false,null,entries,first.ConcurrencyStamp),f.Now,ct);
            await Assert.That(stale.Failure).IsEqualTo(LiveSoloFailure.Conflict);
            await f.PrepareAsync(ct); var activeId = f.Round.QuestionGroupId;
            var active = (await store.GroupsAsync(f.Competition.Id,f.Owner.Id,ct))!.Single(x=>x.Id==activeId);
            var rejected = await store.SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(active.Id,"Unsafe",false,null,entries,active.ConcurrencyStamp),f.Now,ct);
            await Assert.That(rejected.Failure).IsEqualTo(LiveSoloFailure.Conflict);
            await Assert.That((await f.Db.LiveSoloQuestionGroups.SingleAsync(x=>x.Id==first.Id,ct)).Name).IsEqualTo("Reordered");
        });
    }
    [Test, Timeout(300_000)]
    public async Task Rollback_after_reusing_ordered_keys_retains_the_entire_previous_group(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var entries = f.Entries.Select(x => new LiveSoloQuestionGroupEntry(x.Id,null)).ToArray();
            var created = await f.Store(f.Db).SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(null,"Original",false,null,entries,null),f.Now,ct);
            await using var failing = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(f.Options).AddInterceptors(new RejectCommit()).Options);
            var target = created.Group!;
            await Assert.That(async()=>await f.Store(failing).SaveGroupAsync(f.Competition.Id,f.Owner.Id,new(target.Id,"Unsaved",true,25,entries.Reverse().ToArray(),target.ConcurrencyStamp),f.Now,ct)).Throws<InvalidOperationException>();
            await using var read = new NoCtfDbContext(f.Options);
            var persisted = await read.LiveSoloQuestionGroups.Include(x=>x.Items).SingleAsync(ct);
            await Assert.That(persisted.Name).IsEqualTo("Original");
            await Assert.That(persisted.Items.OrderBy(x=>x.Position).Select(x=>x.CompetitionChallengeId)).IsEquivalentTo(entries.Select(x=>x.CompetitionChallengeId));
        });
    }
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData eventData,InterceptionResult result,CancellationToken cancellationToken=default)
            => throw new InvalidOperationException("Injected rollback");
    }
}
