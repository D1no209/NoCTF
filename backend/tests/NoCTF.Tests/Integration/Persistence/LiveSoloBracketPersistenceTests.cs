using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloBracketPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Ordered_Swiss_and_elimination_stages_advance_atomically_without_duplicate_team_slots(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var publisher = new NoCTF.Infrastructure.Messaging.NoOpPostCommitMessagePublisher();
            var configurations = new NoCTF.Infrastructure.Competitions.Configuration.CompetitionConfigurationStore(fixture.Db, publisher,
                new NoCTF.Infrastructure.Competitions.Events.CompetitionEventStore(fixture.Db, publisher));
            var configuration = (LiveSoloCompetitionModeConfiguration)(await configurations.FindAsync(fixture.Competition.Id, ct))!.Configuration;
            configuration.TournamentStages = [new() { CompetitionId = fixture.Competition.Id, Name = "Swiss", Format = LiveSoloBracketFormat.Swiss,
                AdvanceCount = 0, SwissAdvanceWins = 1, SwissEliminateLosses = 2, RequiredWins = 1 },
                new() { CompetitionId = fixture.Competition.Id, Position = 1, Name = "Playoffs", Format = LiveSoloBracketFormat.DoubleElimination, AdvanceCount = 1 },
                new() { CompetitionId = fixture.Competition.Id, Position = 2, Name = "Final", Format = LiveSoloBracketFormat.SingleElimination, AdvanceCount = 1 }];
            configuration.StageRules = [new() { CompetitionId = fixture.Competition.Id, TournamentStage = 2,
                Lane = LiveSoloBracketLane.GrandFinal, Stage = 1, RequiredWins = 2 }];
            await Assert.That((await configurations.TryUpdateAsync(fixture.Competition.Id, configuration, true, fixture.Now, ct)).Failure).IsNull();
            var persisted = (LiveSoloCompetitionModeConfiguration)(await configurations.FindAsync(fixture.Competition.Id, ct))!.Configuration;
            await Assert.That(persisted.TournamentStages.Select(x => x.Name)).IsEquivalentTo(["Swiss", "Playoffs", "Final"]);
            await Assert.That(persisted.StageRules.Single().TournamentStage).IsEqualTo(2);
            var store = fixture.Store(fixture.Db);
            var result = await store.GenerateAsync(new(fixture.Competition.Id, fixture.Owner.Id, fixture.Competition.ConcurrencyStamp,
                [fixture.LeftTeam.Id, fixture.RightTeam.Id], fixture.Now), ct);
            await Assert.That(result.Failure).IsNull();
            for (var step = 0; step < 8; step++)
            {
                fixture.Db.ChangeTracker.Clear();
                var match = await fixture.Db.LiveSoloMatches.Include(x => x.Slots)
                    .Where(x => x.CompetitionId == fixture.Competition.Id && x.SupersededAt == null && x.State == LiveSoloMatchState.Preparing)
                    .OrderBy(x => x.TournamentStage).ThenBy(x => x.Lane).ThenBy(x => x.Stage).FirstOrDefaultAsync(ct);
                if (match is null) break;
                var command = new NoCTF.Application.LiveSolo.Adjudication.AdjudicateLiveSoloMatch(fixture.Competition.Id, match.Id, fixture.Owner.Id,
                    match.ConcurrencyStamp, null, null, null, LiveSoloJudgeAction.ForfeitMatch, match.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId, "Forfeit");
                var decided = await store.ApplyAsync(command, ct);
                await Assert.That(decided.Failure).IsNull();
                if (step == 0)
                {
                    var proposal = new NoCTF.Application.LiveSolo.Adjudication.LiveSoloCorrectionProposal(fixture.Competition.Id,
                        match.Id, fixture.Owner.Id, match.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId!.Value, 0, 0);
                    var preview = await store.PreviewAsync(proposal, ct);
                    await Assert.That(preview!.Downstream.Any()).IsTrue();
                    var begun = await store.BeginCorrectionAsync(new(proposal, preview.PreviewId, "Correct reviewed result"), ct);
                    var correction = begun.Correction!;
                    var applied = await store.ResolveCorrectionAsync(new(fixture.Competition.Id, match.Id, fixture.Owner.Id,
                        correction.Id, correction.ConcurrencyStamp, true, "Rebuild later pairings", []), ct);
                    await Assert.That(applied.Failure).IsNull();
                    await Assert.That(await fixture.Db.LiveSoloMatches.AnyAsync(x => x.SupersededAt != null && x.TournamentStage == 2, ct)).IsTrue();
                }
            }
            fixture.Db.ChangeTracker.Clear();
            var read = await store.ReadAsync(fixture.Competition.Id, fixture.Owner.Id, ct);
            await Assert.That(read!.TournamentStages.Count).IsEqualTo(3);
            await Assert.That(read.TournamentStages.All(x => x.Complete)).IsTrue();
            await Assert.That(read.TournamentStages[0].QualifiedTeamIds.Count).IsEqualTo(2);
            await Assert.That(read.ChampionTeamId).IsNotNull();
            await Assert.That(await fixture.Db.LiveSoloActiveTeamSlots.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task A_two_team_seeded_bracket_rejects_direct_matches_and_a_manual_win_is_not_a_tournament_champion(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var store = fixture.Store(fixture.Db);
            var seeded = await store.GenerateAsync(new(fixture.Competition.Id, fixture.Owner.Id, fixture.Competition.ConcurrencyStamp,
                [fixture.LeftTeam.Id, fixture.RightTeam.Id], fixture.Now), ct);
            await Assert.That(seeded.Failure).IsNull();
            await Assert.That((await store.CreateAsync(new(fixture.Competition.Id, fixture.Owner.Id, fixture.LeftTeam.Id, fixture.RightTeam.Id, 1, fixture.Now), ct)).Failure)
                .IsEqualTo(LiveSoloFailure.Conflict);
            await using var manual = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await manual.PrepareAsync(ct); var match = await manual.Db.LiveSoloMatches.SingleAsync(ct);
            match.State = LiveSoloMatchState.Completed; match.WinnerTeamId = manual.LeftTeam.Id; await manual.Db.SaveChangesAsync(ct);
            await Assert.That((await manual.Store(manual.Db).ReadAsync(manual.Competition.Id, manual.Owner.Id, ct))!.ChampionTeamId).IsNull();
        });
    }
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
