using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class NormalizedScoreboardProjectionTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-19T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LeaderboardProjectionEngine engine = new(new LeaderboardProjectorCatalog());

    [Test]
    public async Task Ctf_projection_is_sparse_deterministic_and_preserves_blood_and_actor_identity()
    {
        var competitionId = Guid.Parse("00000000-0000-0000-0000-000000000100");
        var challengeA = Challenge(1, "Web");
        var challengeB = Challenge(2, "Pwn");
        var actorId = Guid.Parse("00000000-0000-0000-0000-000000000200");
        var teamA = Team(1, "Alpha");
        var teamB = Team(2, "Beta");
        var facts = new[]
        {
            Fact(teamA.Id, challengeA.Id, GameplayFactKind.FlagAttempt, 1,
                GameplayFactResult.Correct, actorId: actorId, submitter: "Player"),
            Fact(teamA.Id, challengeA.Id, GameplayFactKind.HintUnlock, 2,
                GameplayFactResult.Unlocked, actorId: actorId, hintCost: 25),
            Fact(teamB.Id, challengeA.Id, GameplayFactKind.FlagAttempt, 3,
                GameplayFactResult.Correct)
        };
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            [teamA, teamB],
            facts,
            [challengeB, challengeA],
            ProjectedAt: Start.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running);

        var first = engine.ProjectScoreboard(input);
        var replay = engine.ProjectScoreboard(input);

        await Assert.That(first.ChallengeCatalog.Challenges.Select(item => item.Order))
            .IsEquivalentTo([1, 2]);
        await Assert.That(first.Schema.Columns.Select(item => item.Index))
            .IsEquivalentTo([0, 1]);
        await Assert.That(first.Schema.Revision).IsEqualTo(replay.Schema.Revision);
        await Assert.That(first.ChallengeCatalog.Revision).IsEqualTo(replay.ChallengeCatalog.Revision);
        await Assert.That(first.Snapshot.Actors).HasSingleItem();
        await Assert.That(first.Snapshot.Actors[0].UserId).IsEqualTo(actorId);
        await Assert.That(first.Snapshot.Teams.Sum(team => team.Slots.Count)).IsEqualTo(2);

        var alpha = first.Snapshot.Teams.Single(team => team.TeamId == teamA.Id);
        var alphaSlot = alpha.Slots.Single();
        await Assert.That(alphaSlot.ScoreState).IsEqualTo(ScoreboardScoreState.Provisional);
        await Assert.That(alphaSlot.Entries.Single(entry => entry.Kind == ScoreboardEntryKind.Solve).Award)
            .IsEqualTo(ScoreboardAward.FirstBlood);
        await Assert.That(alphaSlot.Breakdowns.Any(item => item.Kind == ScoreboardBreakdownKind.Hint))
            .IsTrue();
        await AssertArithmetic(alpha);
    }

    [Test]
    public async Task Awdp_only_settles_completed_rounds_and_keeps_current_operations_pending()
    {
        var competitionId = Guid.NewGuid();
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha") with { AffectsDynamicChallengeScore = false };
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            60,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Awdp,
            [team],
            [
                Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 10,
                    GameplayFactResult.Correct),
                Fact(team.Id, challenge.Id, GameplayFactKind.FixAttempt, 20,
                    GameplayFactResult.Correct),
                Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 125,
                    GameplayFactResult.Wrong)
            ],
            [challenge],
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(130),
            CompetitionStatus: CompetitionStatus.Running);

        var projection = engine.ProjectScoreboard(input);
        var row = projection.Snapshot.Teams.Single();
        var rounds = projection.Schema.Rounds;

        await Assert.That(rounds.Select(round => round.State))
            .IsEquivalentTo([
                ScoreboardRoundState.Settled,
                ScoreboardRoundState.Settled,
                ScoreboardRoundState.Running
            ]);
        await Assert.That(row.Slots.Count).IsEqualTo(3);
        var settled = row.Slots.Where(slot => slot.ScoreState == ScoreboardScoreState.Settled).ToArray();
        var current = row.Slots.Single(slot => slot.ScoreState == ScoreboardScoreState.Pending);
        await Assert.That(settled.Sum(slot => slot.NetPoints!.Value)).IsEqualTo(280L);
        await Assert.That(settled.All(slot => slot.EntryCount > 0)).IsTrue();
        await Assert.That(settled.All(slot => slot.Entries.Any(entry =>
            (entry.Kind is ScoreboardEntryKind.Attack or ScoreboardEntryKind.Defense)
            && entry.NetPoints.GetValueOrDefault() > 0))).IsTrue();
        await Assert.That(settled.SelectMany(slot => slot.Entries).Select(entry => entry.Id).Distinct().Count())
            .IsEqualTo(settled.SelectMany(slot => slot.Entries).Count());
        await Assert.That(current.EarnedPoints).IsNull();
        await Assert.That(current.DeductedPoints).IsNull();
        await Assert.That(current.NetPoints).IsNull();
        await Assert.That(current.Breakdowns.Single().AttemptCount).IsEqualTo(1);
        await Assert.That(current.Entries.Single().EarnedPoints).IsNull();
        await Assert.That(row.TotalScore).IsEqualTo(280L);
        await Assert.That(row.GlobalAdjustments).IsEmpty();
        await AssertArithmetic(row);
    }

    [Test]
    public async Task Awd_projects_attack_and_availability_into_round_columns()
    {
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var attacker = Team(1, "Attacker");
        var victim = Team(2, "Victim");
        var roundId = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(AwdConfiguration.Default with
        {
            AttackPoints = 50,
            ServiceHealthyPoints = 100
        }, JsonOptions);
        var facts = new[]
        {
            Fact(attacker.Id, challenge.Id, GameplayFactKind.FlagAttempt, 10,
                GameplayFactResult.Correct, referenceKind: GameplayFactReferenceKind.AwdRound,
                referenceId: roundId, victimTeamId: victim.Id),
            Fact(attacker.Id, challenge.Id, GameplayFactKind.AwdServiceTransition, 20,
                GameplayFactResult.ServiceUp),
            Fact(victim.Id, challenge.Id, GameplayFactKind.AwdServiceTransition, 20,
                GameplayFactResult.ServiceUp)
        };
        var rounds = new[]
        {
            new LeaderboardAwdRoundFact(challenge.Id, attacker.Id, roundId, Start, Start.AddSeconds(60)),
            new LeaderboardAwdRoundFact(challenge.Id, victim.Id, roundId, Start, Start.AddSeconds(60))
        };
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(), GameMode.Awd, [attacker, victim], facts, [challenge], configuration,
            Start, AwdRounds: rounds, ProjectedAt: Start.AddSeconds(61),
            CompetitionStatus: CompetitionStatus.Running);

        var projection = engine.ProjectScoreboard(input);
        var attackerRow = projection.Snapshot.Teams.Single(team => team.TeamId == attacker.Id);
        var slot = attackerRow.Slots.Single();

        await Assert.That(projection.Schema.Rounds).HasSingleItem();
        await Assert.That(slot.ScoreState).IsEqualTo(ScoreboardScoreState.Settled);
        await Assert.That(slot.Breakdowns.Any(item => item.Kind == ScoreboardBreakdownKind.Attack))
            .IsTrue();
        await Assert.That(slot.Breakdowns.Any(item => item.Kind == ScoreboardBreakdownKind.Availability))
            .IsTrue();
        await AssertArithmetic(attackerRow);
    }

    [Test]
    public async Task Koh_projects_control_without_round_axis()
    {
        var challenge = Challenge(1, "Misc", "{\"schemaVersion\":1}");
        var team = Team(1, "King");
        var configuration = JsonSerializer.Serialize(new KohConfiguration(1, 5, 10), JsonOptions);
        var projection = engine.ProjectScoreboard(new(
            Guid.NewGuid(),
            GameMode.Koh,
            [team],
            [Fact(team.Id, challenge.Id, GameplayFactKind.KohControlObservation, 5,
                GameplayFactResult.Controlled)],
            [challenge],
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(10),
            CompetitionStatus: CompetitionStatus.Running));

        var row = projection.Snapshot.Teams.Single();
        await Assert.That(projection.Schema.Rounds).IsEmpty();
        await Assert.That(projection.Schema.Columns).HasSingleItem();
        await Assert.That(row.Slots.Single().Breakdowns.Single().Kind)
            .IsEqualTo(ScoreboardBreakdownKind.Control);
        await AssertArithmetic(row);
    }

    [Test]
    public async Task Large_matrix_emits_only_non_empty_slots_and_preserves_all_rows()
    {
        var teams = Enumerable.Range(1, 100).Select(index => Team(index, $"Team {index}")).ToArray();
        var challenges = Enumerable.Range(1, 20).Select(index => Challenge(index, "Misc")).ToArray();
        var projection = engine.ProjectScoreboard(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            teams,
            [Fact(teams[0].Id, challenges[0].Id, GameplayFactKind.FlagAttempt, 1,
                GameplayFactResult.Correct)],
            challenges,
            ProjectedAt: Start.AddSeconds(2),
            CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(projection.Schema.Columns.Count).IsEqualTo(20);
        await Assert.That(projection.Snapshot.Teams.Count).IsEqualTo(100);
        await Assert.That(projection.Snapshot.Teams.Sum(team => team.Slots.Count)).IsEqualTo(1);
        await Assert.That(projection.Snapshot.Teams.SelectMany(team => team.Slots)
            .All(slot => slot.Entries.Count <= 5)).IsTrue();
    }

    [Test]
    public async Task Main_snapshot_hard_caps_slot_entries_and_global_adjustments()
    {
        var team = Team(1, "Alpha");
        var challenge = Challenge(1, "Web");
        var facts = Enumerable.Range(1, 12)
            .Select(index => Fact(
                team.Id,
                challenge.Id,
                GameplayFactKind.HintUnlock,
                index,
                GameplayFactResult.Unlocked,
                actorId: Guid.CreateVersion7(Start.AddSeconds(index)),
                hintCost: 1))
            .Concat(Enumerable.Range(20, 12).Select(index => Fact(
                team.Id,
                null,
                GameplayFactKind.ManualAdjustment,
                index,
                GameplayFactResult.Applied,
                actorId: Guid.CreateVersion7(Start.AddSeconds(index)),
                value: "1")))
            .ToArray();
        var projection = engine.ProjectScoreboard(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [team],
            facts,
            [challenge],
            ProjectedAt: Start.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running));

        var row = projection.Snapshot.Teams.Single();
        var slot = row.Slots.Single();
        await Assert.That(slot.EntryCount).IsEqualTo(12);
        await Assert.That(slot.Entries.Count).IsEqualTo(5);
        await Assert.That(row.GlobalAdjustmentCount).IsEqualTo(12);
        await Assert.That(row.GlobalAdjustments.Count).IsEqualTo(5);
        await Assert.That(projection.Snapshot.Actors.Count).IsEqualTo(10);
        await Assert.That(row.TotalScore).IsEqualTo(0L);
    }

    [Test]
    public async Task Large_round_matrix_keeps_theoretical_cells_sparse()
    {
        var teams = Enumerable.Range(1, 100).Select(index => Team(index, $"Team {index}")).ToArray();
        var challenges = Enumerable.Range(1, 20)
            .Select(index => Challenge(index, "Pwn", "{\"schemaVersion\":4}"))
            .ToArray();
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            60,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var projection = engine.ProjectScoreboard(new(
            Guid.NewGuid(),
            GameMode.Awdp,
            teams,
            [Fact(teams[0].Id, challenges[0].Id, GameplayFactKind.BreakAttempt, 1,
                GameplayFactResult.Correct)],
            challenges,
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(49 * 60 + 1),
            CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(projection.Schema.Rounds.Count).IsEqualTo(50);
        await Assert.That(projection.Schema.Columns.Count).IsEqualTo(1_000);
        await Assert.That(projection.Snapshot.Teams.Count).IsEqualTo(100);
        await Assert.That(projection.Snapshot.Teams.Sum(team => team.Slots.Count)).IsEqualTo(49);
        await Assert.That(projection.Snapshot.Teams.Count(team => team.Slots.Count > 0)).IsEqualTo(1);
    }

    [Test]
    public async Task Banned_and_disqualified_teams_remain_explicit_without_score_slots()
    {
        var challenge = Challenge(1, "Web");
        var banned = Team(1, "Banned") with { IsBanned = true };
        var disqualified = Team(2, "Disqualified") with { AffectsCompetitiveResults = false };
        var projection = engine.ProjectScoreboard(new(
            Guid.NewGuid(), GameMode.Ctf, [banned, disqualified], [], [challenge],
            ProjectedAt: Start, CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(projection.Snapshot.Teams.Single(team => team.TeamId == banned.Id).RankingState)
            .IsEqualTo(ScoreboardRankingState.Banned);
        await Assert.That(projection.Snapshot.Teams.Single(team => team.TeamId == disqualified.Id).RankingState)
            .IsEqualTo(ScoreboardRankingState.Disqualified);
        await Assert.That(projection.Snapshot.Teams.All(team => team.Slots.Count == 0)).IsTrue();
    }

    private static async Task AssertArithmetic(ScoreboardTeam team)
    {
        var slotNet = team.Slots.Aggregate(0L, (total, slot) =>
            checked(total + slot.NetPoints.GetValueOrDefault()));
        var adjustments = team.GlobalAdjustments.Aggregate(0L, (total, item) =>
            checked(total + item.NetPoints));
        await Assert.That(team.TotalScore).IsEqualTo(checked(slotNet + adjustments));
    }

    private static LeaderboardTeamFact Team(int index, string name) => new(
        Guid.Parse($"00000000-0000-0000-0001-{index:D12}"),
        name,
        false,
        false,
        Start.AddMinutes(-1));

    private static LeaderboardChallengeFact Challenge(int order, string direction, string? json = null) =>
        new(
            Guid.Parse($"00000000-0000-0000-0002-{order:D12}"),
            direction,
            $"Challenge {order}",
            false,
            json,
            order,
            true,
            1);

    private static LeaderboardGameplayFact Fact(
        Guid teamId,
        Guid? challengeId,
        GameplayFactKind kind,
        int seconds,
        GameplayFactResult result,
        Guid? actorId = null,
        string? submitter = null,
        long? hintCost = null,
        GameplayFactReferenceKind? referenceKind = null,
        Guid? referenceId = null,
        Guid? victimTeamId = null,
        string? value = null) => new(
            Guid.CreateVersion7(Start.AddSeconds(seconds)),
            teamId,
            challengeId,
            kind,
            Start.AddSeconds(seconds),
            GameplayFactState.Completed,
            result,
            null,
            referenceKind,
            referenceId,
            victimTeamId,
            submitter,
            value,
            HintCost: hintCost,
            ActorUserId: actorId);

    private static ScoreCurveConfiguration FixedCurve(long points) =>
        new(points, points, 2, ScoreDecayMode.Fixed);
}
