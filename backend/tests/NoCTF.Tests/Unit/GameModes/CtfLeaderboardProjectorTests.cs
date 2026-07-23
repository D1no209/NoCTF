using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfLeaderboardProjectorTests
{
    [Test]
    public async Task Projector_uses_configured_dynamic_expression()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(500, 100, 10),
            [],
            "minimumPoints + 17m"));
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(teamId, "red", false, false)],
            [new(
                Guid.NewGuid(), teamId, challengeId, SubmissionKind.Flag, at,
                new ScoringEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = Guid.NewGuid(),
                    TeamId = teamId,
                    CompetitionChallengeId = challengeId,
                    Kind = ScoringEventKind.SubmissionEvaluation,
                    Result = ScoringResult.Correct,
                    OccurredAt = at,
                    CreatedAt = at
                })],
            [],
            [new(challengeId, "Web", false,
                JsonSerializer.Serialize(new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    new(500, 100, 10), [], null, null, "minimumPoints + 17m")))],
            json,
            at.AddMinutes(-1));

        var row = new CtfLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(117);
    }

    [Test]
    public async Task Projector_deduplicates_rejudged_correct_events_and_assigns_blood_slots()
    {
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            [new(firstTeam, "one", false, false), new(secondTeam, "two", false, false)],
            [Fact(firstTeam, start, Guid.NewGuid()), Fact(firstTeam, start.AddSeconds(1), Guid.NewGuid()),
             Fact(secondTeam, start.AddSeconds(2), Guid.NewGuid())],
            [],
            [new(challengeId, "Web", false, JsonSerializer.Serialize(new CtfChallengeConfiguration(
                1, new(100, 100, 10), [new(BloodRewardPolicy.FixedPoints, 10), new(BloodRewardPolicy.FixedPoints, 20)])))],
            JsonSerializer.Serialize(new CtfConfiguration(1, new(100, 100, 10), [])),
            start);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows.Single(row => row.TeamId == firstTeam).Score).IsEqualTo(110);
        await Assert.That(rows.Single(row => row.TeamId == secondTeam).Score).IsEqualTo(120);

        LeaderboardSubmissionFact Fact(Guid teamId, DateTimeOffset at, Guid submissionId) => new(
            submissionId, teamId, challengeId, SubmissionKind.Flag, at,
            new ScoringEvent
            {
                Id = Guid.NewGuid(), CompetitionId = competitionId, TeamId = teamId,
                CompetitionChallengeId = challengeId, Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Correct, OccurredAt = at, CreatedAt = at
            });
    }

    [Test]
    public async Task Projector_applies_only_current_wrong_submission_penalty()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var competitionId = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(
            competitionId, GameMode.Ctf,
            [new(teamId, "red", false, false)],
            [
                Fact(ScoringResult.Correct, at),
                Fact(ScoringResult.Wrong, at.AddSeconds(1))
            ], [],
            [new(challengeId, "Web", false, JsonSerializer.Serialize(
                new CtfChallengeConfiguration(1, new(100, 100, 10), [], null, null, null, 25)))],
            JsonSerializer.Serialize(new CtfConfiguration(1, new(100, 100, 10), [], null, 10)), at);

        var row = new CtfLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(75);

        LeaderboardSubmissionFact Fact(ScoringResult result, DateTimeOffset occurredAt) => new(
            Guid.NewGuid(), teamId, challengeId, SubmissionKind.Flag, occurredAt,
            new ScoringEvent
            {
                Id = Guid.NewGuid(), CompetitionId = competitionId, TeamId = teamId,
                CompetitionChallengeId = challengeId, Kind = ScoringEventKind.SubmissionEvaluation,
                Result = result, OccurredAt = occurredAt, CreatedAt = occurredAt
            });
    }

    [Test]
    public async Task Projector_uses_one_based_distinct_solve_count_for_score_and_blood_value()
    {
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var challengeConfiguration = new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            new(100, 0, 10),
            [
                new(BloodRewardPolicy.FixedPoints, 0),
                new(BloodRewardPolicy.SolveTimePointsPercentage, 50)
            ],
            ScoreExpression: "initialPoints - solveCount * 10m");
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            [new(firstTeam, "first", false, false), new(secondTeam, "second", false, false)],
            [Fact(firstTeam, start), Fact(secondTeam, start.AddSeconds(1))],
            [],
            [new(challengeId, "Web", false, JsonSerializer.Serialize(challengeConfiguration))],
            JsonSerializer.Serialize(new CtfConfiguration(1, new(100, 0, 10), [])),
            start);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows.Single(row => row.TeamId == firstTeam).Score).IsEqualTo(80);
        await Assert.That(rows.Single(row => row.TeamId == secondTeam).Score).IsEqualTo(120);

        LeaderboardSubmissionFact Fact(Guid teamId, DateTimeOffset at) => new(
            Guid.NewGuid(), teamId, challengeId, SubmissionKind.Flag, at,
            new ScoringEvent
            {
                Id = Guid.NewGuid(), CompetitionId = competitionId, TeamId = teamId,
                CompetitionChallengeId = challengeId, Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Correct, OccurredAt = at, CreatedAt = at
            });
    }

    [Test]
    public async Task Fixed_blood_reward_does_not_evaluate_irrelevant_slot_score()
    {
        var teams = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            teams.Select((id, index) => new LeaderboardTeamFact(id, $"team-{index}", false, false)).ToList(),
            teams.Select((id, index) => new LeaderboardSubmissionFact(
                Guid.NewGuid(), id, challengeId, SubmissionKind.Flag, start.AddSeconds(index),
                new ScoringEvent
                {
                    Id = Guid.NewGuid(), CompetitionId = competitionId, TeamId = id,
                    CompetitionChallengeId = challengeId, Kind = ScoringEventKind.SubmissionEvaluation,
                    Result = ScoringResult.Correct, OccurredAt = start.AddSeconds(index), CreatedAt = start
                })).ToList(),
            [],
            [new(challengeId, "Web", false, JsonSerializer.Serialize(new CtfChallengeConfiguration(
                1, new(100, 0, 10),
                [new(BloodRewardPolicy.FixedPoints, 10), new(BloodRewardPolicy.FixedPoints, 20)],
                ScoreExpression: "100m / (solveCount - 2)")))],
            JsonSerializer.Serialize(new CtfConfiguration(1, new(100, 0, 10), [])),
            start);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows.Count).IsEqualTo(3);
    }
}
