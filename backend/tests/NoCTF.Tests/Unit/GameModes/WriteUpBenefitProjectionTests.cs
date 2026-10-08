using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;
using NSubstitute;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class WriteUpBenefitProjectionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-08T01:00:00Z");
    private static ScoreboardProjection Project(LeaderboardProjectionInput input) => new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()).Project(input);

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task All_modes_discount_positive_benefits_without_discounting_manual_adjustments(GameMode mode)
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var facts = new List<LeaderboardGameplayFact>();
        if (mode == GameMode.Ctf) facts.Add(Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct));
        if (mode == GameMode.Awdp)
        {
            facts.Add(Fact(team, challenge, GameplayFactKind.BreakAttempt, GameplayFactResult.Correct));
            facts.Add(Fact(team, challenge, GameplayFactKind.FixAttempt, GameplayFactResult.Correct, Start.AddSeconds(1)));
        }
        if (mode == GameMode.Koh) facts.Add(Fact(team, challenge, GameplayFactKind.KohControlObservation, GameplayFactResult.Controlled));
        facts.Add(Fact(team, challenge, GameplayFactKind.ManualAdjustment, GameplayFactResult.Applied) with { Value = "-37" });
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), mode, [new(team, "team", false, false)], facts,
            [new(challenge, "Web", "challenge", false)], TestConfigurations.Competition(mode), Start,
            ProjectedAt: Start.AddHours(2), CompetitionStatus: CompetitionStatus.Running,
            AwdAggregates: mode == GameMode.Awd ? [new(team, challenge, 310, 300, 1, 1, Start, 500)] : null);
        var plain = Project(input).Snapshot.Teams.Single();
        var taxed = Project(input with { WriteUpUnlocks = [new(Guid.NewGuid(), team, challenge, 20, Start.AddMinutes(1))] });
        var actual = taxed.Snapshot.Teams.Single();
        var gross = plain.ChallengeBenefits.Single().GrossPoints;
        await Assert.That(gross).IsGreaterThan(0);
        var deduction = ChallengeWriteUpPolicy.Deduction(gross, 20);
        await Assert.That(actual.TotalScore).IsEqualTo(plain.TotalScore - deduction);
        await Assert.That(actual.ChallengeBenefits.Single().WriteUpDeductionPoints).IsEqualTo(deduction);
        await Assert.That(actual.ChallengeBenefits.Single().NetPoints).IsEqualTo(actual.TotalScore);
        await Assert.That(actual.Slots.Sum(x => x.NetPoints ?? 0) + actual.ScoreOutsideWindow
            + actual.GlobalAdjustments.Sum(x => x.NetPoints)).IsEqualTo(actual.TotalScore);
    }

    [Test]
    [Arguments(0)]
    [Arguments(20)]
    [Arguments(100)]
    public async Task Reading_before_solving_excludes_blood_but_preserves_completion_and_dynamic_count(int percent)
    {
        var team = Guid.NewGuid(); var other = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf,
            [new(team, "assisted", false, false), new(other, "independent", false, false)],
            [Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct, Start.AddSeconds(5)),
             Fact(other, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct, Start.AddSeconds(10))],
            [new(challenge, "Web", "challenge", false)], ProjectedAt: Start.AddMinutes(1),
            WriteUpUnlocks: [new(Guid.NewGuid(), team, challenge, percent, Start)]);
        var result = Project(input);
        var assisted = result.Snapshot.Teams.Single(x => x.TeamId == team);
        await Assert.That(assisted.Achievements!.Count).IsEqualTo(1);
        await Assert.That(assisted.Slots.SelectMany(x => x.Entries).Any(x => x.Award is not null)).IsFalse();
        await Assert.That(result.Snapshot.Teams.Single(x => x.TeamId == other).Slots.SelectMany(x => x.Entries)
            .Single(x => x.Kind == ScoreboardEntryKind.Solve).Award).IsEqualTo(ScoreboardAward.FirstBlood);
        var baseline = Project(input with { WriteUpUnlocks = null });
        await Assert.That(result.Snapshot.CurrentChallengeScores.Single().Score).IsEqualTo(baseline.Snapshot.CurrentChallengeScores.Single().Score);
    }

    [Test]
    public async Task Reading_after_solving_preserves_blood_and_discounts_its_positive_reward()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf, [new(team, "team", false, false)],
            [Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct)],
            [new(challenge, "Web", "challenge", false)], ProjectedAt: Start.AddMinutes(1),
            WriteUpUnlocks: [new(Guid.NewGuid(), team, challenge, 20, Start.AddSeconds(1))]);
        var row = Project(input).Snapshot.Teams.Single();
        await Assert.That(row.Slots.SelectMany(x => x.Entries).Single(x => x.Kind == ScoreboardEntryKind.Solve).Award).IsEqualTo(ScoreboardAward.FirstBlood);
        await Assert.That(row.TotalScore).IsEqualTo(row.ChallengeBenefits.Single().GrossPoints - row.ChallengeBenefits.Single().WriteUpDeductionPoints);
    }

    [Test]
    public async Task Unsolved_receipt_does_not_create_negative_score_or_completion()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var row = Project(new(Guid.NewGuid(), GameMode.Ctf, [new(team, "team", false, false)], [],
            [new(challenge, "Web", "challenge", false)], ProjectedAt: Start.AddMinutes(1),
            WriteUpUnlocks: [new(Guid.NewGuid(), team, challenge, 20, Start)])).Snapshot.Teams.Single();
        await Assert.That(row.TotalScore).IsEqualTo(0);
        await Assert.That(row.Achievements!).IsEmpty();
        await Assert.That(row.ChallengeBenefits.Single().WriteUpDeductionPercent).IsEqualTo(20);
        await Assert.That(row.ChallengeBenefits.Single().NetPoints).IsEqualTo(0);
    }

    private static LeaderboardGameplayFact Fact(Guid team, Guid challenge, GameplayFactKind kind, GameplayFactResult result,
        DateTimeOffset? at = null) => new(Guid.NewGuid(), team, challenge, kind, at ?? Start,
            GameplayFactState.Completed, result, null);

    [Test]
    [Arguments(0)]
    [Arguments(20)]
    [Arguments(100)]
    public async Task Ctf_trend_does_not_charge_future_benefits_at_an_earlier_read_time(int percent)
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid(); var solveAt = Start.AddMinutes(10);
        var projection = Project(new(Guid.NewGuid(), GameMode.Ctf, [new(team, "team", false, false)],
            [Fact(team, challenge, GameplayFactKind.FlagAttempt, GameplayFactResult.Correct, solveAt)],
            [new(challenge, "Web", "challenge", false)], ProjectedAt: Start.AddHours(1),
            WriteUpUnlocks: [new(Guid.NewGuid(), team, challenge, percent, Start)]));
        var reader = Substitute.For<IScoreboardTrendFactReader>();
        reader.ReadManualAdjustmentsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoreboardTrendAdjustment>>([]));
        var trend = (await new BuildScoreboardTrends(reader).ExecuteAsync(projection))!.Teams.Single();
        await Assert.That(trend.Points.All(x => x.Score >= 0 && x.At >= solveAt)).IsTrue();
        await Assert.That(trend.Points.LastOrDefault()?.Score ?? 0).IsEqualTo(projection.Snapshot.Teams.Single().TotalScore);
    }

    [Test]
    public async Task Awdp_long_history_allocates_one_total_deduction_across_round_windows()
    {
        var team = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awdp, [new(team, "team", false, false)],
            [Fact(team, challenge, GameplayFactKind.BreakAttempt, GameplayFactResult.Correct),
             Fact(team, challenge, GameplayFactKind.FixAttempt, GameplayFactResult.Correct, Start.AddSeconds(1)),
             Fact(team, challenge, GameplayFactKind.HintUnlock, GameplayFactResult.Unlocked) with { HintCost = 7 },
             Fact(team, challenge, GameplayFactKind.ManualAdjustment, GameplayFactResult.Applied) with { Value = "-13" }],
            [new(challenge, "Web", "challenge", false)], TestConfigurations.Competition(GameMode.Awdp), Start,
            ProjectedAt: Start.AddHours(10), CompetitionStatus: CompetitionStatus.Running,
            WriteUpUnlocks: [new(Guid.NewGuid(), team, challenge, 33, Start.AddMinutes(1))]);
        var projection = Project(input); var row = projection.Snapshot.Teams.Single(); var benefit = row.ChallengeBenefits.Single();
        await Assert.That(projection.Schema.RoundWindowStart.GetValueOrDefault()).IsGreaterThan(1);
        await Assert.That(benefit.WriteUpDeductionPoints).IsEqualTo(ChallengeWriteUpPolicy.Deduction(benefit.GrossPoints, 33));
        await Assert.That(benefit.NetPoints).IsEqualTo(benefit.GrossPoints - benefit.WriteUpDeductionPoints - 20);
        await Assert.That(row.TotalScore).IsEqualTo(benefit.NetPoints);
        await Assert.That(row.Slots.Sum(x => x.NetPoints ?? 0) + row.ScoreOutsideWindow
            + row.GlobalAdjustments.Sum(x => x.NetPoints)).IsEqualTo(row.TotalScore);
        var column = row.Slots.First().ColumnIndex;
        var page = ScoreboardSlotDetailProjection.Project(projection, team, column, row.Slots.First().ScoreState,
            null, [], null, null, 50);
        await Assert.That(page.Entries.Any(x => x.Kind == ScoreboardEntryKind.WriteUp)).IsTrue();
    }
}
