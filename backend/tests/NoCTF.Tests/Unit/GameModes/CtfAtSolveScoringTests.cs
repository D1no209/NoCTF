using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfAtSolveScoringTests
{
    private static readonly Guid ChallengeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid[] Teams = Enumerable.Range(1, 5)
        .Select(i => Guid.Parse($"20000000-0000-0000-0000-{i:000000000000}")).ToArray();
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2026-10-02T00:00:00Z");

    [Test, Arguments(CtfInteractionKind.FlagSubmission), Arguments(CtfInteractionKind.PatchVerification)]
    public async Task Later_solves_do_not_reprice_earlier_solves_and_quote_the_next_price(CtfInteractionKind interaction)
    {
        var first = Input([Fact(0, 1, interaction)], interaction: interaction);
        var before = Project(first);
        var after = Project(first with { GameplayFacts = [.. first.GameplayFacts, Fact(1, 2, interaction), Fact(2, 3, interaction), Fact(0, 4, interaction)] });
        await Assert.That(Score(before, 0)).IsEqualTo(500);
        await Assert.That(Score(after, 0)).IsEqualTo(500);
        await Assert.That(Score(after, 1)).IsEqualTo(400);
        await Assert.That(Score(after, 2)).IsEqualTo(300);
        await Assert.That(before.CurrentScores![ChallengeId]).IsEqualTo(400);
        await Assert.That(after.CurrentScores![ChallengeId]).IsEqualTo(200);
        await Assert.That(after.Entries.Single(team => team.TeamId == Teams[0]).SolveCount).IsEqualTo(1);
    }

    [Test]
    public async Task Existing_dynamic_mode_keeps_repricing_all_solvers()
    {
        var input = Input([Fact(0, 1), Fact(1, 2), Fact(2, 3)], CtfScoreSettlementMode.DynamicRecalculation);
        var result = Project(input);
        await Assert.That(result.Entries.Take(3).All(team => team.Score == 300)).IsTrue();
        await Assert.That(result.CurrentScores![ChallengeId]).IsEqualTo(300);
    }

    [Test]
    public async Task A_track_without_decay_impact_receives_the_quote_without_consuming_it()
    {
        var input = Input([Fact(0, 1), Fact(1, 2), Fact(2, 3)]) with
        {
            Teams = [new(Teams[0], "guest", false, false, EarnsBlood: false, AffectsDynamicChallengeScore: false),
                new(Teams[1], "first", false, false), new(Teams[2], "second", false, false)]
        };
        var result = Project(input);
        await Assert.That(Score(result, 0)).IsEqualTo(500);
        await Assert.That(Score(result, 1)).IsEqualTo(500);
        await Assert.That(Score(result, 2)).IsEqualTo(400);
        await Assert.That(result.CurrentScores![ChallengeId]).IsEqualTo(300);
    }

    [Test]
    public async Task Non_scoring_teams_can_affect_decay_independently_of_blood_rank()
    {
        var input = Input([Fact(0, 1), Fact(1, 2)]) with
        {
            Teams = [new(Teams[0], "observer", false, false, EarnsScore: false, EarnsBlood: false),
                new(Teams[1], "scoring", false, false)]
        };
        await Assert.That(Score(Project(input), 1)).IsEqualTo(400);
    }

    [Test]
    public async Task Equal_timestamps_follow_fact_identity_and_ignore_input_order()
    {
        var input = Input([Fact(1, 1) with { GameplayFactId = Guid.Parse("30000000-0000-0000-0000-000000000002") },
            Fact(0, 1) with { GameplayFactId = Guid.Parse("30000000-0000-0000-0000-000000000001") }]);
        var result = Project(input);
        await Assert.That(Score(result, 0)).IsEqualTo(500);
        await Assert.That(Score(result, 1)).IsEqualTo(400);
    }

    [Test]
    public async Task Challenge_override_and_restored_inheritance_select_the_effective_mode()
    {
        var input = Input([Fact(0, 1), Fact(1, 2)]);
        var rules = new CtfCompetitionChallengeRules { ScoreSettlementMode = CtfScoreSettlementMode.DynamicRecalculation };
        input = input with { Challenges = [input.Challenges!.Single() with { Rules = rules }] };
        await Assert.That(Score(Project(input), 0)).IsEqualTo(400);
        rules.ScoreSettlementMode = null;
        await Assert.That(Score(Project(input), 0)).IsEqualTo(500);
    }

    [Test]
    public async Task Management_changes_and_rejudgement_rebuild_existing_scores()
    {
        var input = Input([Fact(0, 1), Fact(1, 2), Fact(2, 3)]);
        var configuration = (CtfCompetitionModeConfiguration)input.CompetitionConfiguration!;
        await Assert.That(Score(Project(input), 0)).IsEqualTo(500);
        configuration.ScoreSettlementMode = CtfScoreSettlementMode.DynamicRecalculation;
        await Assert.That(Score(Project(input), 0)).IsEqualTo(300);
        configuration.ScoreSettlementMode = CtfScoreSettlementMode.AtSolve;
        var rejudged = input with { GameplayFacts = input.GameplayFacts.Select(fact => fact.TeamId == Teams[0]
            ? fact with { Result = GameplayFactResult.Wrong } : fact).ToArray() };
        await Assert.That(Score(Project(rejudged), 1)).IsEqualTo(500);
        configuration.DefaultScoreCurve.InitialPoints = 900;
        await Assert.That(Score(Project(rejudged), 1)).IsEqualTo(900);
    }

    [Test]
    public async Task Custom_curve_keeps_each_solve_position_and_quotes_the_next_one()
    {
        var input = Input([Fact(0, 1), Fact(1, 2)]);
        var configuration = (CtfCompetitionModeConfiguration)input.CompetitionConfiguration!;
        configuration.DefaultScoreCurve.DecayMode = PersistedScoreDecayMode.Custom;
        configuration.DefaultScoreCurve.CustomExpression = "initialPoints - (solveCount - 1) * 25";
        var result = Project(input);
        await Assert.That(Score(result, 0)).IsEqualTo(500);
        await Assert.That(Score(result, 1)).IsEqualTo(475);
        await Assert.That(result.CurrentScores![ChallengeId]).IsEqualTo(450);
    }

    [Test, Arguments(CompetitionBloodRewardPolicy.CurrentPointsPercentage), Arguments(CompetitionBloodRewardPolicy.SolveTimePointsPercentage)]
    public async Task Percentage_blood_and_breakdown_are_fixed_to_each_solve_price(CompetitionBloodRewardPolicy policy)
    {
        var input = Input([Fact(0, 1), Fact(1, 2), Fact(2, 3)]);
        ((CtfCompetitionModeConfiguration)input.CompetitionConfiguration!).BloodRewards = Enumerable.Range(0, 3)
            .Select(position => new CompetitionBloodReward { Position = position, Policy = policy, Value = 10 }).ToList();
        var result = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);
        var entry = result.Snapshot.Teams.Single(team => team.TeamId == Teams[0]).Slots.SelectMany(slot => slot.Entries).Single();
        await Assert.That(entry.EarnedPoints).IsEqualTo(550);
        await Assert.That(entry.AwardPoints).IsEqualTo(50);
        var second = result.Snapshot.Teams.Single(team => team.TeamId == Teams[1]).Slots.SelectMany(slot => slot.Entries).Single();
        await Assert.That(second.EarnedPoints).IsEqualTo(440);
        await Assert.That(second.AwardPoints).IsEqualTo(40);
    }

    private static GameModeLeaderboardProjection Project(LeaderboardProjectionInput input) => new CtfLeaderboardProjector().Project(input);
    private static long Score(GameModeLeaderboardProjection projection, int team) => projection.Entries.Single(entry => entry.TeamId == Teams[team]).Score;
    private static LeaderboardGameplayFact Fact(int team, int seconds, CtfInteractionKind interaction = CtfInteractionKind.FlagSubmission) =>
        new(Guid.NewGuid(), Teams[team], ChallengeId, CtfCompletionEligibility.CompletionKind(interaction), Started.AddSeconds(seconds),
            GameplayFactState.Completed, GameplayFactResult.Correct, null);
    private static LeaderboardProjectionInput Input(IReadOnlyList<LeaderboardGameplayFact> facts,
        CtfScoreSettlementMode mode = CtfScoreSettlementMode.AtSolve, CtfInteractionKind interaction = CtfInteractionKind.FlagSubmission) =>
        new(Guid.NewGuid(), GameMode.Ctf, Teams.Select((team, i) => new LeaderboardTeamFact(team, $"team-{i}", false, false)).ToArray(), facts,
            [new(ChallengeId, "Web", "challenge", false, InteractionKind: interaction)],
            new CtfCompetitionModeConfiguration { ScoreSettlementMode = mode,
                DefaultScoreCurve = new() { InitialPoints = 500, MinimumPoints = 100, DecayTeamCount = 5, DecayMode = PersistedScoreDecayMode.Linear } },
            ProjectedAt: Started.AddHours(1));
}
