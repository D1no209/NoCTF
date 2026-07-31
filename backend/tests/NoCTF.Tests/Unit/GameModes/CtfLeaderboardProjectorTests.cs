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
    public async Task Projection_engine_exposes_first_second_and_third_bloods_for_each_challenge()
    {
        var teams = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000004")
        };
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Ctf,
            teams.Select((teamId, index) => new LeaderboardTeamFact(
                teamId,
                $"team-{index + 1}",
                false,
                false,
                start)).ToList(),
            [
                Fact(teams[0], start, Guid.Parse("10000000-0000-0000-0000-000000000001")),
                Fact(teams[0], start.AddMilliseconds(500), Guid.Parse("10000000-0000-0000-0000-000000000002")),
                Fact(teams[1], start.AddSeconds(1), Guid.Parse("20000000-0000-0000-0000-000000000001")),
                Fact(teams[2], start.AddSeconds(1), Guid.Parse("30000000-0000-0000-0000-000000000001")),
                Fact(teams[3], start.AddSeconds(3), Guid.Parse("40000000-0000-0000-0000-000000000001"))
            ],
            [],
            [new(challengeId, "Web", false, JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    new(100, 100, 10),
                    [])))],
            JsonSerializer.Serialize(new CtfConfiguration(
                CtfConfiguration.CurrentSchemaVersion,
                new(100, 100, 10),
                [])),
            start);

        var projection = new LeaderboardProjectionEngine(
            new LeaderboardProjectorCatalog()).Project(input);

        await Assert.That(projection.Bloods).Count().IsEqualTo(3);
        await Assert.That(projection.Bloods[0].TeamId).IsEqualTo(teams[0]);
        await Assert.That(projection.Bloods[0].BloodRank)
            .IsEqualTo(LeaderboardBloodRank.First);
        await Assert.That(projection.Bloods[1].TeamId).IsEqualTo(teams[1]);
        await Assert.That(projection.Bloods[1].BloodRank)
            .IsEqualTo(LeaderboardBloodRank.Second);
        await Assert.That(projection.Bloods[2].TeamId).IsEqualTo(teams[2]);
        await Assert.That(projection.Bloods[2].BloodRank)
            .IsEqualTo(LeaderboardBloodRank.Third);
        foreach (var blood in projection.Bloods)
        {
            var slot = projection.Subjects
                .Single(subject => subject.SubjectId == blood.TeamId)
                .Slots
                .Single(candidate => candidate.SlotKey == blood.SlotKey);
            await Assert.That(slot.BloodRank).IsEqualTo(blood.BloodRank);
            await Assert.That(slot.BloodAt).IsEqualTo(blood.OccurredAt);
        }
        var fourthSlot = projection.Subjects
            .Single(subject => subject.SubjectId == teams[3])
            .Slots
            .Single();
        await Assert.That(fourthSlot.BloodRank).IsNull();
        await Assert.That(fourthSlot.BloodAt).IsNull();

        LeaderboardSubmissionFact Fact(
            Guid teamId,
            DateTimeOffset receivedAt,
            Guid submissionId) =>
            new(
                submissionId,
                teamId,
                challengeId,
                SubmissionKind.Flag,
                receivedAt,
                new ScoringEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = teamId,
                    CompetitionChallengeId = challengeId,
                    Kind = ScoringEventKind.SubmissionEvaluation,
                    Result = ScoringResult.Correct,
                    OccurredAt = receivedAt,
                    CreatedAt = receivedAt
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

    [Test]
    public async Task Earlier_last_submission_time_wins_even_when_evaluation_finishes_later()
    {
        var earlier = Guid.NewGuid();
        var later = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            competitionId,
            [
                new(earlier, "z", false, false, at),
                new(later, "a", false, false, at)
            ],
            [
                Fact(earlier, challengeId, at, at.AddSeconds(3), competitionId),
                Fact(later, challengeId, at.AddSeconds(1), at.AddSeconds(2), competitionId)
            ],
            [],
            [challengeId]);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(earlier);
        await Assert.That(rows[0].LastScoreAt).IsEqualTo(at);
        await Assert.That(rows[1].TeamId).IsEqualTo(later);
    }

    [Test]
    public async Task More_solves_win_after_score_and_last_solve_time_tie()
    {
        var fewerSolves = Guid.NewGuid();
        var moreSolves = Guid.NewGuid();
        var firstChallengeId = Guid.NewGuid();
        var secondChallengeId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            competitionId,
            [
                new(fewerSolves, "a", false, false, at),
                new(moreSolves, "z", false, false, at)
            ],
            [
                Fact(fewerSolves, firstChallengeId, at.AddSeconds(1), at.AddSeconds(1), competitionId),
                Fact(moreSolves, firstChallengeId, at, at, competitionId),
                Fact(moreSolves, secondChallengeId, at.AddSeconds(1), at.AddSeconds(1), competitionId)
            ],
            [
                new(new ScoringEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = moreSolves,
                    Kind = ScoringEventKind.HintUnlock,
                    Result = ScoringResult.Correct,
                    OccurredAt = at,
                    CreatedAt = at
                }, CurrentValue: 100)
            ],
            [firstChallengeId, secondChallengeId]);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(moreSolves);
        await Assert.That(rows[0].Score).IsEqualTo(100);
        await Assert.That(rows[0].SolveCount).IsEqualTo(2);
        await Assert.That(rows[1].TeamId).IsEqualTo(fewerSolves);
    }

    [Test]
    public async Task Registration_time_then_team_id_break_remaining_ties()
    {
        var earlyRegistration = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var competitionId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            competitionId,
            [
                new(earlyRegistration, "z", false, false, at.AddMinutes(-1)),
                new(lowerId, "z", false, false, at),
                new(higherId, "a", false, false, at)
            ],
            [],
            [],
            []);

        var rows = new CtfLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(earlyRegistration);
        await Assert.That(rows[1].TeamId).IsEqualTo(lowerId);
        await Assert.That(rows[2].TeamId).IsEqualTo(higherId);
    }

    private static LeaderboardProjectionInput Input(
        Guid competitionId,
        IReadOnlyList<LeaderboardTeamFact> teams,
        IReadOnlyList<LeaderboardSubmissionFact> submissions,
        IReadOnlyList<LeaderboardSystemFact> systemEvents,
        IReadOnlyList<Guid> challengeIds) =>
        new(
            competitionId,
            GameMode.Ctf,
            teams,
            submissions,
            systemEvents,
            challengeIds.Select(challengeId => new LeaderboardChallengeFact(
                challengeId,
                "Web",
                false,
                JsonSerializer.Serialize(new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    new(100, 100, 10),
                    []))))
                .ToList(),
            JsonSerializer.Serialize(new CtfConfiguration(
                CtfConfiguration.CurrentSchemaVersion,
                new(100, 100, 10),
                [])));

    private static LeaderboardSubmissionFact Fact(
        Guid teamId,
        Guid challengeId,
        DateTimeOffset receivedAt,
        DateTimeOffset occurredAt,
        Guid competitionId) =>
        new(
            Guid.NewGuid(),
            teamId,
            challengeId,
            SubmissionKind.Flag,
            receivedAt,
            new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                CompetitionChallengeId = challengeId,
                TeamId = teamId,
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Correct,
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            });
}
