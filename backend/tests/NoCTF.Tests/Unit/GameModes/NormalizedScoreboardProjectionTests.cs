using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
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
    private static ScoreboardProjection ProjectNormalized(
        LeaderboardProjectionEngine engine,
        LeaderboardProjectionInput input) => engine.ProjectOutputs(input).Scoreboard;

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-08-19T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LeaderboardProjectionEngine engine = new(new LeaderboardProjectorCatalog());

    [Test]
    public async Task Ctf_achievements_keep_first_solver_even_when_compact_entries_are_truncated()
    {
        var team = Team(1, "Solved"); var challenge = Challenge(1, "Web"); var unsolved = Challenge(2, "Pwn");
        var first = Fact(team.Id, challenge.Id, GameplayFactKind.FlagAttempt, 1, GameplayFactResult.Correct,
            Guid.NewGuid(), "First solver");
        var facts = new[] { first,
            Fact(team.Id, challenge.Id, GameplayFactKind.FlagAttempt, 2, GameplayFactResult.Correct, Guid.NewGuid(), "Later solver"),
            Fact(team.Id, unsolved.Id, GameplayFactKind.ManualAdjustment, 3, GameplayFactResult.Applied, value: "9999"),
            Fact(team.Id, unsolved.Id, GameplayFactKind.FlagAttempt, 4, GameplayFactResult.Correct) with { State = GameplayFactState.Processing } }
            .Concat(Enumerable.Range(10, 10).Select(second => Fact(team.Id, challenge.Id, GameplayFactKind.FlagAttempt,
                second, GameplayFactResult.Wrong))).ToArray();
        var board = ProjectNormalized(engine, new(Guid.NewGuid(), GameMode.Ctf, [team], facts, [challenge, unsolved],
            ProjectedAt: Start.AddMinutes(1), CompetitionStatus: CompetitionStatus.Running));
        var achievement = board.Snapshot.Teams.Single().Achievements!.Single();
        await Assert.That(achievement.CompetitionChallengeId).IsEqualTo(challenge.Id);
        await Assert.That(achievement.UserId).IsEqualTo(first.ActorUserId);
        await Assert.That(achievement.DisplayName).IsEqualTo("First solver");
        await Assert.That(achievement.OccurredAt).IsEqualTo(first.OccurredAt);
        await Assert.That(achievement.Kind).IsEqualTo(ScoreboardEntryKind.Solve);
    }

    [Test]
    public async Task Awdp_achievements_retain_both_actors_and_original_times_outside_round_window()
    {
        var team = Team(1, "Historical"); var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var attack = Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 1, GameplayFactResult.Correct, Guid.NewGuid(), "Attacker");
        var defense = Fact(team.Id, challenge.Id, GameplayFactKind.FixAttempt, 2, GameplayFactResult.Correct, Guid.NewGuid(), "Defender");
        var config = JsonSerializer.Serialize(new AwdpConfiguration(AwdpConfiguration.CurrentSchemaVersion, 60,
            FixedCurve(100), FixedCurve(40), RequireBreakBeforeFix: false), JsonOptions);
        var board = ProjectNormalized(engine, new(Guid.NewGuid(), GameMode.Awdp, [team], [attack, defense], [challenge], config, Start,
            ProjectedAt: Start.AddHours(5), CompetitionStatus: CompetitionStatus.Running, ScoreboardGameplayFacts: []));
        var achievements = board.Snapshot.Teams.Single().Achievements!;
        await Assert.That(achievements.Count).IsEqualTo(2);
        await Assert.That(achievements.Single(x => x.Kind == ScoreboardEntryKind.Attack).DisplayName).IsEqualTo("Attacker");
        await Assert.That(achievements.Single(x => x.Kind == ScoreboardEntryKind.Defense).OccurredAt).IsEqualTo(defense.OccurredAt);
        var defenseOnly = ProjectNormalized(engine, new(Guid.NewGuid(), GameMode.Awdp, [team], [defense], [challenge], config, Start,
            ProjectedAt: Start.AddHours(5), CompetitionStatus: CompetitionStatus.Running));
        await Assert.That(defenseOnly.Snapshot.Teams.Single().Achievements!.Single().Kind).IsEqualTo(ScoreboardEntryKind.Defense);
    }

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

        var first = ProjectNormalized(engine, input);
        var replay = ProjectNormalized(engine, input);

        await Assert.That(first.ChallengeCatalog.Challenges.Select(item => item.Order))
            .IsEquivalentTo([1, 2]);
        await Assert.That(first.Schema.Columns.Select(item => item.Index))
            .IsEquivalentTo([0, 1]);
        await Assert.That(first.Schema.Revision).IsEqualTo(replay.Schema.Revision);
        await Assert.That(first.ChallengeCatalog.Revision).IsEqualTo(replay.ChallengeCatalog.Revision);
        await Assert.That(first.Snapshot.Actors).HasSingleItem();
        await Assert.That(first.Snapshot.Actors[0].UserId).IsEqualTo(actorId);
        await Assert.That(first.Snapshot.Teams.Sum(team => team.Slots.Count)).IsEqualTo(2);
        var currentScores = first.Snapshot.CurrentChallengeScores
            .ToDictionary(score => score.CompetitionChallengeId);
        await Assert.That(currentScores[challengeA.Id].Score).IsEqualTo(495L);
        await Assert.That(currentScores[challengeB.Id].Score).IsEqualTo(500L);

        var alpha = first.Snapshot.Teams.Single(team => team.TeamId == teamA.Id);
        var alphaSlot = alpha.Slots.Single();
        await Assert.That(alphaSlot.ScoreState).IsEqualTo(ScoreboardScoreState.Provisional);
        await Assert.That(alphaSlot.Entries.Single(entry => entry.Kind == ScoreboardEntryKind.Solve).Award)
            .IsEqualTo(ScoreboardAward.FirstBlood);
        await Assert.That(alphaSlot.Breakdowns.Any(item => item.Kind == ScoreboardBreakdownKind.Hint))
            .IsTrue();
        await Assert.That(alpha.MemberContributions).HasSingleItem();
        await Assert.That(alpha.MemberContributions[0].UserId).IsEqualTo(actorId);
        await Assert.That(alpha.MemberContributions[0].DisplayName).IsEqualTo("Player");
        await Assert.That(alpha.MemberContributions[0].EarnedPoints)
            .IsEqualTo(alphaSlot.EarnedPoints!.Value);
        await AssertArithmetic(alpha);
    }

    [Test]
    public async Task Ctf_member_contributions_group_all_positive_points_by_submitter()
    {
        var team = Team(1, "Contributors");
        var web = Challenge(1, "Web");
        var pwn = Challenge(2, "Pwn");
        var misc = Challenge(3, "Misc");
        var alice = Guid.Parse("00000000-0000-0000-0003-000000000001");
        var bob = Guid.Parse("00000000-0000-0000-0003-000000000002");
        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [team],
            [
                Fact(team.Id, web.Id, GameplayFactKind.FlagAttempt, 1,
                    GameplayFactResult.Correct, actorId: alice, submitter: "Alice"),
                Fact(team.Id, pwn.Id, GameplayFactKind.FlagAttempt, 2,
                    GameplayFactResult.Correct, actorId: bob, submitter: "Bob"),
                Fact(team.Id, misc.Id, GameplayFactKind.FlagAttempt, 3,
                    GameplayFactResult.Correct, actorId: alice, submitter: "Alice"),
                Fact(team.Id, pwn.Id, GameplayFactKind.FlagAttempt, 4,
                    GameplayFactResult.Wrong, actorId: alice, submitter: "Alice")
            ],
            [web, pwn, misc],
            ProjectedAt: Start.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running));

        var row = projection.Snapshot.Teams.Single();
        var contributions = row.MemberContributions.ToDictionary(item => item.UserId);
        await Assert.That(contributions).Count().IsEqualTo(2);
        await Assert.That(contributions[alice].DisplayName).IsEqualTo("Alice");
        await Assert.That(contributions[bob].DisplayName).IsEqualTo("Bob");
        await Assert.That(contributions[alice].EarnedPoints)
            .IsGreaterThan(contributions[bob].EarnedPoints);
        await Assert.That(contributions.Values.Sum(item => item.EarnedPoints))
            .IsEqualTo(row.Slots.Sum(slot => slot.EarnedPoints.GetValueOrDefault()));
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

        var projection = ProjectNormalized(engine, input);
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
        var orderedSlots = row.Slots.OrderBy(slot => slot.ColumnIndex).ToArray();
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
        await Assert.That(orderedSlots.All(slot =>
            slot.OffenseState == ScoreboardOperationState.Succeeded)).IsTrue();
        await Assert.That(orderedSlots.All(slot =>
            slot.DefenseState == ScoreboardOperationState.Succeeded)).IsTrue();
        await Assert.That(current.Breakdowns.Single().SuccessfulCount).IsEqualTo(0);
        await Assert.That(row.TotalScore).IsEqualTo(280L);
        await Assert.That(row.GlobalAdjustments).IsEmpty();
        await AssertArithmetic(row);
    }

    [Test]
    public async Task Awdp_success_state_carries_into_a_later_round_without_new_operations()
    {
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha");
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            60,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Awdp,
            [team],
            [Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 10,
                GameplayFactResult.Correct)],
            [challenge],
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(130),
            CompetitionStatus: CompetitionStatus.Running));

        var current = projection.Snapshot.Teams.Single().Slots
            .Single(slot => slot.ScoreState == ScoreboardScoreState.Pending);
        await Assert.That(current.EntryCount).IsEqualTo(0);
        await Assert.That(current.Breakdowns).IsEmpty();
        await Assert.That(current.OffenseState).IsEqualTo(ScoreboardOperationState.Succeeded);
        await Assert.That(current.DefenseState).IsEqualTo(ScoreboardOperationState.None);
    }

    [Test]
    public async Task Awdp_paused_round_keeps_a_future_end_boundary_for_the_frozen_remaining_time()
    {
        var competitionId = Guid.NewGuid();
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            60,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var pausedAt = Start.AddSeconds(90);
        var projectedAt = Start.AddSeconds(120);
        var projection = ProjectNormalized(engine, new(
            competitionId,
            GameMode.Awdp,
            [Team(1, "Alpha")],
            [],
            [challenge],
            configuration,
            Start,
            LifecycleAudits:
            [
                new()
                {
                    Id = Guid.CreateVersion7(Start),
                    CompetitionId = competitionId,
                    From = CompetitionStatus.Published,
                    To = CompetitionStatus.Running,
                    OccurredAt = Start
                },
                new()
                {
                    Id = Guid.CreateVersion7(pausedAt),
                    CompetitionId = competitionId,
                    From = CompetitionStatus.Running,
                    To = CompetitionStatus.Paused,
                    OccurredAt = pausedAt
                }
            ],
            ProjectedAt: projectedAt,
            CompetitionStatus: CompetitionStatus.Paused));

        var rounds = projection.Schema.Rounds.OrderBy(round => round.Number).ToArray();
        await Assert.That(rounds.Length).IsEqualTo(2);
        await Assert.That(rounds[0].State).IsEqualTo(ScoreboardRoundState.Settled);
        await Assert.That(rounds[1].State).IsEqualTo(ScoreboardRoundState.Running);
        await Assert.That(rounds[1].StartAt).IsEqualTo(Start.AddSeconds(60));
        await Assert.That(rounds[1].EndAt).IsEqualTo(projectedAt.AddSeconds(30));
        await Assert.That(rounds[1].EndAt > rounds[1].StartAt).IsTrue();
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

        var projection = ProjectNormalized(engine, input);
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
    public async Task Awd_explicit_window_keeps_authoritative_total_and_absolute_round_numbers()
    {
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha");
        var configuration = JsonSerializer.Serialize(AwdConfiguration.Default with
        {
            ServiceHealthyPoints = 100
        }, JsonOptions);
        var rounds = Enumerable.Range(51, ScoreboardRoundWindow.DefaultSize)
            .Select(number => new LeaderboardAwdRoundFact(
                challenge.Id,
                team.Id,
                AwdRoundSpecificationId.FromRound(number).Value,
                Start.AddSeconds((number - 1) * 60),
                Start.AddSeconds(number * 60)))
            .ToArray();
        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Awd,
            [team],
            [Fact(team.Id, challenge.Id, GameplayFactKind.AwdServiceTransition, 1,
                GameplayFactResult.ServiceUp)],
            [challenge],
            configuration,
            Start,
            AwdRounds: rounds,
            ProjectedAt: Start.AddSeconds(121 * 60),
            CompetitionStatus: CompetitionStatus.Running,
            ScoreboardRoundWindowEnd: 100,
            ScoreboardLatestRound: 120,
            AwdAggregates:
            [
                new(team.Id, challenge.Id, 6_000, 0, 0, 60, null)
            ]));

        await Assert.That(projection.Schema.Rounds.Select(round => round.Number))
            .IsEquivalentTo(Enumerable.Range(51, ScoreboardRoundWindow.DefaultSize));
        await Assert.That(projection.Schema.RoundWindowStart).IsEqualTo(51);
        await Assert.That(projection.Schema.RoundWindowEnd).IsEqualTo(100);
        await Assert.That(projection.Schema.LatestRound).IsEqualTo(120);
        var row = projection.Snapshot.Teams.Single();
        await Assert.That(row.TotalScore).IsEqualTo(6_000);
        await Assert.That(row.ScoreOutsideWindow).IsEqualTo(1_000);
        await AssertArithmetic(row);
    }

    [Test]
    public async Task Schema_revision_changes_when_round_metadata_changes_without_changing_columns()
    {
        var competitionId = Guid.NewGuid();
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha");
        var roundId = Guid.NewGuid();
        var round = new LeaderboardAwdRoundFact(
            challenge.Id,
            team.Id,
            roundId,
            Start,
            Start.AddSeconds(60));
        var before = ProjectNormalized(engine, new(
            competitionId,
            GameMode.Awd,
            [team],
            [],
            [challenge],
            JsonSerializer.Serialize(AwdConfiguration.Default, JsonOptions),
            Start,
            AwdRounds: [round],
            ProjectedAt: Start.AddSeconds(59),
            CompetitionStatus: CompetitionStatus.Running));
        var after = ProjectNormalized(engine, new(
            competitionId,
            GameMode.Awd,
            [team],
            [],
            [challenge],
            JsonSerializer.Serialize(AwdConfiguration.Default, JsonOptions),
            Start,
            AwdRounds: [round],
            ProjectedAt: Start.AddSeconds(61),
            CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(before.Schema.Columns).IsEquivalentTo(after.Schema.Columns);
        await Assert.That(before.Schema.Rounds.Single().State)
            .IsEqualTo(ScoreboardRoundState.Running);
        await Assert.That(after.Schema.Rounds.Single().State)
            .IsEqualTo(ScoreboardRoundState.Settled);
        await Assert.That(before.Schema.Revision).IsNotEqualTo(after.Schema.Revision);
        await Assert.That(ProjectNormalized(engine, new(
                competitionId,
                GameMode.Awd,
                [team],
                [],
                [challenge],
                JsonSerializer.Serialize(AwdConfiguration.Default, JsonOptions),
                Start,
                AwdRounds: [round],
                ProjectedAt: Start.AddSeconds(61),
                CompetitionStatus: CompetitionStatus.Running)).Schema.Revision)
            .IsEqualTo(after.Schema.Revision);
    }

    [Test]
    public async Task Challenge_catalog_revision_covers_effective_title_and_direction()
    {
        var competitionId = Guid.NewGuid();
        var original = Challenge(1, "Web");
        var renamed = original with { Title = "Renamed", Direction = "Pwn" };
        var before = ProjectNormalized(engine, new(
            competitionId,
            GameMode.Ctf,
            [Team(1, "Alpha")],
            [],
            [original],
            ProjectedAt: Start,
            CompetitionStatus: CompetitionStatus.Running));
        var after = ProjectNormalized(engine, new(
            competitionId,
            GameMode.Ctf,
            [Team(1, "Alpha")],
            [],
            [renamed],
            ProjectedAt: Start,
            CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(before.ChallengeCatalog.Revision)
            .IsNotEqualTo(after.ChallengeCatalog.Revision);
        await Assert.That(after.ChallengeCatalog.Challenges.Single().Title).IsEqualTo("Renamed");
        await Assert.That(after.ChallengeCatalog.Challenges.Single().Direction).IsEqualTo("Pwn");
    }

    [Test]
    public async Task Koh_projects_control_without_round_axis()
    {
        var challenge = Challenge(1, "Misc", "{\"schemaVersion\":1}");
        var team = Team(1, "King");
        var configuration = JsonSerializer.Serialize(new KohConfiguration(1, 5, 10), JsonOptions);
        var projection = ProjectNormalized(engine, new(
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
        var projection = ProjectNormalized(engine, new(
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
                challenge.Id,
                GameplayFactKind.ManualAdjustment,
                index,
                GameplayFactResult.Applied,
                actorId: Guid.CreateVersion7(Start.AddSeconds(index)),
                value: "1")))
            .ToArray();
        var projection = ProjectNormalized(engine, new(
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
        await Assert.That(projection.DetailActors.Count).IsEqualTo(10);
        await Assert.That(projection.EntryAllocations).HasSingleItem();
        await Assert.That(projection.EntryAllocations.Single().Entry.ActorIndex).IsNull();
        await Assert.That(projection.EntryAllocations.Single().Source!.ActorUserId).IsNull();
        await Assert.That(projection.EntryAllocations.All(entry => entry.Entry.NetPoints == -1)).IsTrue();
        await Assert.That(row.TotalScore).IsEqualTo(0L);
    }

    [Test]
    public async Task Grouped_fact_multiplicity_is_preserved_in_counts_and_detail_sources()
    {
        var team = Team(1, "Alpha");
        var challenge = Challenge(1, "Web");
        var hint = Fact(
            team.Id,
            challenge.Id,
            GameplayFactKind.HintUnlock,
            1,
            GameplayFactResult.Unlocked,
            hintCost: 2) with
        {
            Multiplicity = 10,
            LastOccurredAt = Start.AddSeconds(10)
        };
        var adjustment = Fact(
            team.Id,
            challenge.Id,
            GameplayFactKind.ManualAdjustment,
            20,
            GameplayFactResult.Applied,
            value: "3") with
        {
            Multiplicity = 7,
            LastOccurredAt = Start.AddSeconds(26)
        };

        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [team],
            [hint, adjustment],
            [challenge],
            ProjectedAt: Start.AddMinutes(1),
            CompetitionStatus: CompetitionStatus.Running));
        var row = projection.Snapshot.Teams.Single();
        var slot = row.Slots.Single();
        var source = projection.EntryAllocations.Single().Source;

        await Assert.That(slot.EntryCount).IsEqualTo(10);
        await Assert.That(slot.DeductedPoints).IsEqualTo(20);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.Multiplicity).IsEqualTo(10);
        await Assert.That(source.DeductedPointsPerOccurrence).IsEqualTo(2);
        await Assert.That(row.GlobalAdjustmentCount).IsEqualTo(7);
        await Assert.That(row.TotalScore).IsEqualTo(1);
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
        var projection = ProjectNormalized(engine, new(
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
        await Assert.That(projection.Snapshot.Teams.Sum(team => team.Slots.Count)).IsEqualTo(50);
        await Assert.That(projection.Snapshot.Teams.Count(team => team.Slots.Count > 0)).IsEqualTo(1);
    }

    [Test]
    public async Task Awdp_long_history_keeps_only_the_latest_bounded_round_window()
    {
        const int elapsedSeconds = 30 * 24 * 60 * 60;
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha");
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            1,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Awdp,
            [team],
            [
                Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 1,
                    GameplayFactResult.Correct),
                Fact(team.Id, challenge.Id, GameplayFactKind.FixAttempt, 2,
                    GameplayFactResult.Correct)
            ],
            [challenge],
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(elapsedSeconds),
            CompetitionStatus: CompetitionStatus.Running));

        await Assert.That(projection.Schema.Rounds.Count).IsEqualTo(ScoreboardRoundWindow.DefaultSize);
        await Assert.That(projection.Schema.RoundWindowStart).IsEqualTo(elapsedSeconds - 48);
        await Assert.That(projection.Schema.RoundWindowEnd).IsEqualTo(elapsedSeconds + 1);
        await Assert.That(projection.Schema.LatestRound).IsEqualTo(elapsedSeconds + 1);
        await Assert.That(projection.Schema.Columns.Count).IsEqualTo(ScoreboardRoundWindow.DefaultSize);
        var row = projection.Snapshot.Teams.Single();
        await Assert.That(row.TotalScore)
            .IsEqualTo(100L * (elapsedSeconds - 1) + 40L * (elapsedSeconds - 2));
        await Assert.That(row.AttackScore).IsEqualTo(100L * (elapsedSeconds - 1));
        await Assert.That(row.DefenseScore).IsEqualTo(40L * (elapsedSeconds - 2));
        await Assert.That(row.ChallengeScores.Single()).IsEqualTo(new ScoreboardChallengeScore(
            challenge.Id,
            100L * (elapsedSeconds - 1),
            40L * (elapsedSeconds - 2)));
        await Assert.That(row.ScoreOutsideWindow).IsGreaterThan(0);
        await AssertArithmetic(row);
    }

    [Test]
    public async Task Awdp_explicit_window_keeps_authoritative_total_and_projects_requested_rounds()
    {
        const int elapsedSeconds = 1_000;
        var challenge = Challenge(1, "Pwn", "{\"schemaVersion\":4}");
        var team = Team(1, "Alpha");
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            1,
            FixedCurve(100),
            FixedCurve(40),
            RequireBreakBeforeFix: false), JsonOptions);
        var projection = ProjectNormalized(engine, new(
            Guid.NewGuid(),
            GameMode.Awdp,
            [team],
            [Fact(team.Id, challenge.Id, GameplayFactKind.BreakAttempt, 1,
                GameplayFactResult.Correct)],
            [challenge],
            configuration,
            Start,
            ProjectedAt: Start.AddSeconds(elapsedSeconds),
            CompetitionStatus: CompetitionStatus.Running,
            ScoreboardRoundWindowEnd: 100));

        await Assert.That(projection.Schema.Rounds.Select(round => round.Number))
            .IsEquivalentTo(Enumerable.Range(51, 50));
        await Assert.That(projection.Schema.RoundWindowStart).IsEqualTo(51);
        await Assert.That(projection.Schema.RoundWindowEnd).IsEqualTo(100);
        await Assert.That(projection.Schema.LatestRound).IsEqualTo(elapsedSeconds + 1);
        await Assert.That(projection.Snapshot.Teams.Single().TotalScore)
            .IsEqualTo(100L * (elapsedSeconds - 1));
        await Assert.That(projection.Snapshot.Teams.Single().ScoreOutsideWindow)
            .IsEqualTo(100L * (elapsedSeconds - 1 - ScoreboardRoundWindow.DefaultSize));
        await AssertArithmetic(projection.Snapshot.Teams.Single());
    }

    [Test]
    public async Task Banned_and_disqualified_teams_remain_explicit_without_score_slots()
    {
        var challenge = Challenge(1, "Web");
        var banned = Team(1, "Banned") with { IsBanned = true };
        var disqualified = Team(2, "Disqualified") with { AffectsCompetitiveResults = false };
        var projection = ProjectNormalized(engine, new(
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
        await Assert.That(team.TotalScore)
            .IsEqualTo(checked(slotNet + adjustments + team.ScoreOutsideWindow));
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
            true);

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
