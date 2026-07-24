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
