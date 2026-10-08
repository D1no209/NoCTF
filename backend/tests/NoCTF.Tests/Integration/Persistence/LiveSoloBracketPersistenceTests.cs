using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloBracketPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task A_durable_match_win_advances_opponents_and_moves_exclusive_team_slots_in_the_same_transaction(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            ((LiveSoloCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).BracketFormat = LiveSoloBracketFormat.DoubleElimination;
            await fixture.Db.SaveChangesAsync(ct);
            var store = fixture.Store(fixture.Db);
            var command = new GenerateLiveSoloBracket(fixture.Competition.Id, fixture.Owner.Id, fixture.Competition.ConcurrencyStamp,
                [fixture.LeftTeam.Id, fixture.RightTeam.Id], fixture.Now);
            var generated = await store.GenerateAsync(command, ct);
            await Assert.That(generated.Failure).IsNull(); await Assert.That(generated.Bracket!.ChampionTeamId).IsNull();
            await Assert.That(await fixture.Db.LiveSoloActiveTeamSlots.CountAsync(ct)).IsEqualTo(2);
            await Assert.That((await store.GenerateAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
            var first = generated.Bracket.Matches.Single(x => x.Match.State == LiveSoloMatchState.Preparing).Match;
            await fixture.PrepareAsync(ct, first.Id); await fixture.StartAsync(ct);
            store = fixture.Store(fixture.Db);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var accepted = await store.AdmitAsync(new(fixture.Competition.Id, first.Id, fixture.Round.Id, question.Id, fixture.Left.Id, "flag{first}", fixture.Now), ct);
            await Assert.That(accepted.Failure).IsNull(); await store.ResolveAsync(fixture.Round.Id, fixture.Now, ct);
            fixture.Db.ChangeTracker.Clear();
            var graph = await store.ReadAsync(fixture.Competition.Id, fixture.Owner.Id, ct);
            var final = await fixture.Db.LiveSoloMatches.Include(x => x.Slots).SingleAsync(x => x.Lane == LiveSoloBracketLane.GrandFinal, ct);
            await Assert.That(final.State).IsEqualTo(LiveSoloMatchState.Preparing);
            await Assert.That(final.Slots.Single(x => x.Side == LiveSoloSide.Left).TeamId).IsEqualTo(fixture.LeftTeam.Id);
            await Assert.That(final.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId).IsEqualTo(fixture.RightTeam.Id);
            await Assert.That((await fixture.Db.LiveSoloActiveTeamSlots.Select(x => x.MatchId).ToArrayAsync(ct)).All(x => x == final.Id)).IsTrue();
            await Assert.That(graph!.ChampionTeamId).IsNull();
        });
    }
}
