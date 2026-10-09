using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloHintPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Published_hints_are_shared_free_by_both_sides_and_do_not_create_gameplay_or_acquisition_facts(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var entry = await f.Db.CompetitionChallenges.SingleAsync(x=>x.Id==f.Entries[0].Id,ct);
            entry.Hints.AddRange([new() {Id=Guid.NewGuid(),Content="Visible hint",Cost=500,PublishedAt=f.Now},
                new() {Id=Guid.NewGuid(),Content="Hidden",PublishedAt=f.Now,HiddenAt=f.Now},new() {Id=Guid.NewGuid(),Content="Future",PublishedAt=f.Now.AddHours(1)}]);
            foreach (var hint in entry.Hints) f.Db.Entry(hint).State=EntityState.Added;
            await f.Db.SaveChangesAsync(ct); await f.PrepareAsync(ct); await f.StartAsync(ct);
            var reader = new LiveSoloHintReader(f.Db,new LiveSoloExecutionAccess(f.Db),f.Clock);
            var question = f.Round.Questions.Single(x=>x.Position==0);
            var request = new LiveSoloResourceRequest(f.Competition.Id,f.Match.Id,f.Round.Id,question.Id,f.Left.Id,f.Now);
            var left = await reader.ReadAsync(request,ct); var right = await reader.ReadAsync(request with {ActorId=f.Right.Id},ct);
            await Assert.That(left!.Single().Content).IsEqualTo("Visible hint"); await Assert.That(right).IsEquivalentTo(left!);
            await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloDownloadEvidences.CountAsync(ct)).IsEqualTo(0);
            (await f.Db.LiveSoloMatches.SingleAsync(ct)).State=LiveSoloMatchState.Paused;
            (await f.Db.Competitions.SingleAsync(ct)).Status=CompetitionStatus.Paused; await f.Db.SaveChangesAsync(ct);
            await Assert.That((await reader.ReadAsync(request,ct))!.Count).IsEqualTo(1);
            entry.Hints[0].HiddenAt=f.Now;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(request,ct)).IsEmpty();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Unopened_cross_round_match_and_banned_roster_requests_cannot_return_hint_content(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct); await f.StartAsync(ct);
            var reader=new LiveSoloHintReader(f.Db,new LiveSoloExecutionAccess(f.Db),f.Clock);
            var question=f.Round.Questions.Single(x=>x.Position==0);var unopened=f.Round.Questions.Single(x=>x.Position==1);
            var request=new LiveSoloResourceRequest(f.Competition.Id,f.Match.Id,f.Round.Id,question.Id,f.Left.Id,f.Now);
            await Assert.That(await reader.ReadAsync(request with {QuestionId=unopened.Id},ct)).IsNull();
            await Assert.That(await reader.ReadAsync(request with {CompetitionId=Guid.NewGuid()},ct)).IsNull();
            await Assert.That(await reader.ReadAsync(request with {MatchId=Guid.NewGuid()},ct)).IsNull();
            await Assert.That(await reader.ReadAsync(request with {RoundId=Guid.NewGuid()},ct)).IsNull();
            await Assert.That(await reader.ReadAsync(request with {ActorId=f.Owner.Id},ct)).IsNull();
            (await f.Db.Teams.SingleAsync(x=>x.Id==f.LeftTeam.Id,ct)).IsBanned=true;await f.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(request,ct)).IsNull();
        });
    }
}
