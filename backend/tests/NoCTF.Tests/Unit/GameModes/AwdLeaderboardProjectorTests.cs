using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdLeaderboardProjectorTests
{
    [Test]
    public async Task Split_pool_is_shared_by_distinct_attackers_and_victim_is_debited_once()
    {
        var attackerA = Guid.NewGuid();
        var attackerB = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var roundId = AwdRoundSpecificationId.FromRound(4).Value;
        var now = DateTimeOffset.UtcNow;
        var configuration = JsonSerializer.Serialize(new AwdConfiguration(
            AwdConfiguration.CurrentSchemaVersion,
            0,
            300,
            AttackRewardMode.SplitVictimDefensePool,
            999,
            101,
            30,
            0,
            0), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awd,
            [new(attackerA, "a", false, false), new(attackerB, "b", false, false), new(victim, "v", false, false)],
            [
                Attack(attackerA, victim, challengeId, roundId, now),
                Attack(attackerA, victim, challengeId, roundId, now.AddSeconds(1)),
                Attack(attackerB, victim, challengeId, roundId, now.AddSeconds(2))
            ],
            [],
            [new(challengeId, "Pwn", false)],
            configuration,
            now.AddMinutes(-10));

        var rows = new AwdLeaderboardProjector().Project(input).ToDictionary(row => row.TeamId);

        await Assert.That(rows[attackerA].Score).IsEqualTo(50);
        await Assert.That(rows[attackerB].Score).IsEqualTo(50);
        await Assert.That(rows[victim].Score).IsEqualTo(-101);
    }

    [Test]
    public async Task Service_score_uses_only_complete_rounds_and_state_before_round_end()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var configuration = JsonSerializer.Serialize(new AwdConfiguration(
            AwdConfiguration.CurrentSchemaVersion,
            HardeningDurationSeconds: 0,
            RoundDurationSeconds: 10,
            AttackRewardMode.FixedPerAttack,
            AttackPoints: 0,
            VictimDefensePoolPoints: 0,
            CheckerIntervalSeconds: 5,
            ServiceHealthyPoints: 10,
            ServiceUnhealthyPenalty: 5));
        var events = new[]
        {
            ServiceState(teamId, challengeId, competitionId, ScoringResult.Wrong, start.AddSeconds(15)),
            ServiceState(teamId, challengeId, competitionId, ScoringResult.Correct, start.AddSeconds(20))
        };
        var rounds = new[]
        {
            Round(teamId, challengeId, start, start.AddSeconds(10)),
            Round(teamId, challengeId, start.AddSeconds(10), start.AddSeconds(20)),
            Round(teamId, challengeId, start.AddSeconds(20), start.AddSeconds(30))
        };
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Awd,
            [new(teamId, "team", false, false, start)],
            [],
            events,
            [new(challengeId, "Pwn", false)],
            configuration,
            start,
            AwdRounds: rounds,
            ProjectedAt: start.AddSeconds(25));

        var row = new AwdLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(5);
    }

    [Test]
    public async Task Attack_points_then_up_round_count_break_total_score_ties()
    {
        var attacker = Guid.NewGuid();
        var lowerAttackPoints = Guid.NewGuid();
        var upTeam = Guid.NewGuid();
        var idleTeam = Guid.NewGuid();
        var attackChallengeId = Guid.NewGuid();
        var serviceChallengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var rows = Project(
            [
                new(attacker, "z-attacker", false, false, at),
                new(lowerAttackPoints, "a-lower", false, false, at),
                new(upTeam, "z-up", false, false, at),
                new(idleTeam, "a-idle", false, false, at)
            ],
            [
                Attack(attacker, idleTeam, attackChallengeId, Guid.NewGuid(), at),
                Attack(attacker, idleTeam, attackChallengeId, Guid.NewGuid(), at.AddSeconds(1)),
                Attack(lowerAttackPoints, idleTeam, attackChallengeId, Guid.NewGuid(), at.AddSeconds(1))
            ],
            [
                Hint(attacker, 20, at),
                Hint(lowerAttackPoints, 10, at),
                Hint(upTeam, 10, at)
            ],
            [Round(upTeam, serviceChallengeId, at, at.AddSeconds(10))],
            at.AddSeconds(11),
            attackPoints: 10,
            serviceHealthyPoints: 10);

        await Assert.That(rows.Single(row => row.TeamId == attacker).Rank)
            .IsLessThan(rows.Single(row => row.TeamId == lowerAttackPoints).Rank);
        await Assert.That(rows.Single(row => row.TeamId == upTeam).Rank)
            .IsLessThan(rows.Single(row => row.TeamId == idleTeam).Rank);
    }

    [Test]
    public async Task More_first_attacks_win_after_score_attack_points_and_up_rounds_tie()
    {
        var moreAttacks = Guid.NewGuid();
        var fewerAttacks = Guid.NewGuid();
        var firstVictim = Guid.NewGuid();
        var secondVictim = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var rows = Project(
            [
                new(moreAttacks, "z", false, false, at),
                new(fewerAttacks, "a", false, false, at),
                new(firstVictim, "victim-1", false, false, at),
                new(secondVictim, "victim-2", false, false, at)
            ],
            [
                Attack(moreAttacks, firstVictim, challengeId, Guid.NewGuid(), at),
                Attack(moreAttacks, secondVictim, challengeId, Guid.NewGuid(), at.AddSeconds(1)),
                Attack(fewerAttacks, firstVictim, challengeId, Guid.NewGuid(), at.AddSeconds(1))
            ],
            [],
            [],
            at.AddSeconds(2));

        await Assert.That(rows.Single(row => row.TeamId == moreAttacks).Rank)
            .IsLessThan(rows.Single(row => row.TeamId == fewerAttacks).Rank);
    }

    [Test]
    public async Task Earlier_last_attack_time_wins_after_higher_ties()
    {
        var earlier = Guid.NewGuid();
        var later = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var rows = Project(
            [
                new(earlier, "z", false, false, at),
                new(later, "a", false, false, at),
                new(victim, "victim", false, false, at)
            ],
            [
                Attack(earlier, victim, challengeId, Guid.NewGuid(), at),
                Attack(later, victim, challengeId, Guid.NewGuid(), at.AddSeconds(1))
            ],
            [],
            [],
            at.AddSeconds(2));

        await Assert.That(rows.Single(row => row.TeamId == earlier).Rank)
            .IsLessThan(rows.Single(row => row.TeamId == later).Rank);
    }

    [Test]
    public async Task Registration_time_then_team_id_break_remaining_ties()
    {
        var earlyRegistration = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var rows = Project(
            [
                new(earlyRegistration, "z", false, false, at.AddMinutes(-1)),
                new(lowerId, "z", false, false, at),
                new(higherId, "a", false, false, at)
            ],
            [],
            [],
            [],
            at);

        await Assert.That(rows[0].TeamId).IsEqualTo(earlyRegistration);
        await Assert.That(rows[1].TeamId).IsEqualTo(lowerId);
        await Assert.That(rows[2].TeamId).IsEqualTo(higherId);
    }

    private static IReadOnlyList<LeaderboardEntry> Project(
        IReadOnlyList<LeaderboardTeamFact> teams,
        IReadOnlyList<LeaderboardSubmissionFact> attacks,
        IReadOnlyList<LeaderboardSystemFact> systemEvents,
        IReadOnlyList<LeaderboardAwdRoundFact> rounds,
        DateTimeOffset projectedAt,
        long attackPoints = 0,
        long serviceHealthyPoints = 0)
    {
        var challengeIds = attacks
            .Select(attack => attack.CompetitionChallengeId!.Value)
            .Concat(rounds.Select(round => round.CompetitionChallengeId))
            .Distinct()
            .ToList();
        var configuration = JsonSerializer.Serialize(new AwdConfiguration(
            AwdConfiguration.CurrentSchemaVersion,
            HardeningDurationSeconds: 0,
            RoundDurationSeconds: 10,
            AttackRewardMode.FixedPerAttack,
            AttackPoints: attackPoints,
            VictimDefensePoolPoints: 0,
            CheckerIntervalSeconds: 5,
            ServiceHealthyPoints: serviceHealthyPoints,
            ServiceUnhealthyPenalty: 0));
        return new AwdLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Awd,
            teams,
            attacks,
            systemEvents,
            challengeIds.Select(id => new LeaderboardChallengeFact(id, "Pwn", false)).ToList(),
            configuration,
            projectedAt.AddHours(-1),
            AwdRounds: rounds,
            ProjectedAt: projectedAt));
    }

    private static LeaderboardSystemFact Hint(
        Guid teamId,
        long cost,
        DateTimeOffset at) =>
        new(
            new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = Guid.NewGuid(),
                TeamId = teamId,
                Kind = ScoringEventKind.HintUnlock,
                Result = ScoringResult.Correct,
                OccurredAt = at,
                CreatedAt = at
            },
            cost);

    private static LeaderboardSystemFact ServiceState(
        Guid teamId,
        Guid challengeId,
        Guid competitionId,
        ScoringResult result,
        DateTimeOffset at) =>
        new(new ScoringEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Kind = ScoringEventKind.AwdServiceStatus,
            Result = result,
            OccurredAt = at,
            CreatedAt = at
        });

    private static LeaderboardAwdRoundFact Round(
        Guid teamId,
        Guid challengeId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(
            challengeId,
            teamId,
            AwdRoundSpecificationId.FromRound((int)(startsAt.ToUnixTimeSeconds() % 10_000)).Value,
            startsAt,
            endsAt);

    private static LeaderboardSubmissionFact Attack(
        Guid attacker,
        Guid victim,
        Guid challengeId,
        Guid roundId,
        DateTimeOffset at) =>
        new(Guid.NewGuid(), attacker, challengeId, SubmissionKind.Flag, at,
            new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = Guid.NewGuid(),
                TeamId = attacker,
                VictimTeamId = victim,
                CompetitionChallengeId = challengeId,
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Correct,
                SpecificationKind = SpecificationKind.AwdRound,
                SpecificationId = roundId,
                OccurredAt = at,
                CreatedAt = at
            }, victim);
}
