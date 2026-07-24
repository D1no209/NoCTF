using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class KohLeaderboardProjectorTests
{
    [Test]
    public async Task Projector_uses_challenge_points_override_including_explicit_zero()
    {
        var teamId = Guid.NewGuid();
        var defaultChallengeId = Guid.NewGuid();
        var overriddenChallengeId = Guid.NewGuid();
        var zeroChallengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            [new(teamId, "team", false, false, at.AddMinutes(-1))],
            [
                (teamId, defaultChallengeId, at),
                (teamId, overriddenChallengeId, at.AddSeconds(1)),
                (teamId, zeroChallengeId, at.AddSeconds(2))
            ],
            new Dictionary<Guid, long?>
            {
                [defaultChallengeId] = null,
                [overriddenChallengeId] = 25,
                [zeroChallengeId] = 0
            },
            defaultPoints: 10);

        var row = new KohLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(35);
        await Assert.That(row.SolveCount).IsEqualTo(3);
    }

    [Test]
    public async Task More_controlled_observations_win_when_scores_tie()
    {
        var oneControl = Guid.NewGuid();
        var twoControls = Guid.NewGuid();
        var paidChallengeId = Guid.NewGuid();
        var zeroChallengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            [
                new(oneControl, "one", false, false, at),
                new(twoControls, "two", false, false, at)
            ],
            [
                (oneControl, paidChallengeId, at),
                (twoControls, paidChallengeId, at),
                (twoControls, zeroChallengeId, at.AddSeconds(1))
            ],
            new Dictionary<Guid, long?>
            {
                [paidChallengeId] = 10,
                [zeroChallengeId] = 0
            });

        var rows = new KohLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(twoControls);
        await Assert.That(rows[1].TeamId).IsEqualTo(oneControl);
    }

    [Test]
    public async Task More_distinct_controlled_challenges_win_when_scores_and_counts_tie()
    {
        var twoChallenges = Guid.NewGuid();
        var threeChallenges = Guid.NewGuid();
        var paidChallengeId = Guid.NewGuid();
        var firstZeroChallengeId = Guid.NewGuid();
        var secondZeroChallengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            [
                new(twoChallenges, "a", false, false, at),
                new(threeChallenges, "z", false, false, at)
            ],
            [
                (twoChallenges, paidChallengeId, at),
                (twoChallenges, firstZeroChallengeId, at.AddSeconds(1)),
                (twoChallenges, firstZeroChallengeId, at.AddSeconds(2)),
                (threeChallenges, paidChallengeId, at),
                (threeChallenges, firstZeroChallengeId, at.AddSeconds(1)),
                (threeChallenges, secondZeroChallengeId, at.AddSeconds(2))
            ],
            new Dictionary<Guid, long?>
            {
                [paidChallengeId] = 10,
                [firstZeroChallengeId] = 0,
                [secondZeroChallengeId] = 0
            });

        var rows = new KohLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(threeChallenges);
        await Assert.That(rows[1].TeamId).IsEqualTo(twoChallenges);
    }

    [Test]
    public async Task Earlier_first_control_wins_after_score_count_and_challenge_ties()
    {
        var earlier = Guid.NewGuid();
        var later = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            [
                new(earlier, "later-name", false, false, at),
                new(later, "earlier-name", false, false, at)
            ],
            [
                (earlier, challengeId, at),
                (earlier, challengeId, at.AddSeconds(2)),
                (later, challengeId, at.AddSeconds(1)),
                (later, challengeId, at.AddSeconds(2))
            ],
            new Dictionary<Guid, long?> { [challengeId] = 10 });

        var rows = new KohLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(earlier);
        await Assert.That(rows[1].TeamId).IsEqualTo(later);
    }

    [Test]
    public async Task Registration_time_then_team_id_break_remaining_ties()
    {
        var earlyRegistration = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var challengeId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var input = Input(
            [
                new(earlyRegistration, "z", false, false, at.AddMinutes(-1)),
                new(lowerId, "z", false, false, at),
                new(higherId, "a", false, false, at)
            ],
            [
                (earlyRegistration, challengeId, at),
                (lowerId, challengeId, at),
                (higherId, challengeId, at)
            ],
            new Dictionary<Guid, long?> { [challengeId] = 10 });

        var rows = new KohLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(earlyRegistration);
        await Assert.That(rows[1].TeamId).IsEqualTo(lowerId);
        await Assert.That(rows[2].TeamId).IsEqualTo(higherId);
    }

    private static LeaderboardProjectionInput Input(
        IReadOnlyList<LeaderboardTeamFact> teams,
        IReadOnlyList<(Guid TeamId, Guid ChallengeId, DateTimeOffset OccurredAt)> observations,
        IReadOnlyDictionary<Guid, long?> challengePoints,
        long defaultPoints = 0)
    {
        var competitionId = Guid.NewGuid();
        var challenges = challengePoints.Select(item => new LeaderboardChallengeFact(
            item.Key,
            "Pwn",
            false,
            JsonSerializer.Serialize(new KohChallengeConfiguration(
                KohChallengeConfiguration.CurrentSchemaVersion,
                ControlPointsPerInterval: item.Value))))
            .ToList();
        var facts = observations.Select(observation => new LeaderboardSystemFact(
            new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                CompetitionChallengeId = observation.ChallengeId,
                TeamId = observation.TeamId,
                Kind = ScoringEventKind.KohObservation,
                Result = ScoringResult.Correct,
                OccurredAt = observation.OccurredAt,
                CreatedAt = observation.OccurredAt
            }))
            .ToList();
        return new(
            competitionId,
            GameMode.Koh,
            teams,
            [],
            facts,
            challenges,
            JsonSerializer.Serialize(new KohConfiguration(
                KohConfiguration.CurrentSchemaVersion,
                PollIntervalSeconds: 5,
                ControlPointsPerInterval: defaultPoints)));
    }
}
