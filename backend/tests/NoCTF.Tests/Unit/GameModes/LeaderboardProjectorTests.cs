using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Ctf.Configuration;
using System.Text.Json;

namespace NoCTF.Tests.Unit.GameModes;

public class LeaderboardProjectorTests
{
    [Test]
    public async Task Project_UsesStableSubmissionOrderingAndIgnoresBannedTeams()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Ctf,
            [new(first, "alpha", false, false), new(second, "banned", true, false)],
            [
                Fact(first, 2, ScoringResult.Correct),
                Fact(first, 1, ScoringResult.Correct),
                Fact(second, 0, ScoringResult.Correct)
            ], []);

        var result = new CtfLeaderboardProjector().Project(input);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].TeamId).IsEqualTo(first);
        await Assert.That(result[0].Score).IsEqualTo(1000L);
        await Assert.That(result[0].Rank).IsEqualTo(1);
    }

    [Test]
    public async Task Project_IncludesSystemFactsWithoutSubmission()
    {
        var team = Guid.NewGuid();
        var system = new ScoringEvent { Id = Guid.NewGuid(), TeamId = team, Result = ScoringResult.Correct, OccurredAt = DateTimeOffset.UtcNow };
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awd,
            [new(team, "alpha", false, false)], [], [new(system)]);

        var result = new AwdLeaderboardProjector().Project(input);

        await Assert.That(result[0].Score).IsEqualTo(1L);
        await Assert.That(result[0].SolveCount).IsEqualTo(0);
    }

    [Test]
    public async Task CtfProjector_IgnoresSystemFacts()
    {
        var team = Guid.NewGuid();
        var system = new ScoringEvent
        {
            Id = Guid.NewGuid(),
            TeamId = team,
            Result = ScoringResult.Correct,
            OccurredAt = DateTimeOffset.UtcNow
        };

        var result = new CtfLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(team, "alpha", false, false)],
            [],
            [new(system)]));

        await Assert.That(result[0].Score).IsEqualTo(0L);
    }

    [Test]
    public async Task KohProjector_UsesSystemFactsWithoutManualSubmissions()
    {
        var team = Guid.NewGuid();
        var system = new ScoringEvent
        {
            Id = Guid.NewGuid(),
            TeamId = team,
            Result = ScoringResult.Correct,
            OccurredAt = DateTimeOffset.UtcNow
        };

        var result = new KohLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Koh,
            [new(team, "alpha", false, false)],
            [Fact(team, 1, ScoringResult.Correct)],
            [new(system)]));

        await Assert.That(result[0].Score).IsEqualTo(1L);
        await Assert.That(result[0].SolveCount).IsEqualTo(0);
    }

    [Test]
    public async Task Project_FiltersDeletedChallengesAndMapsDirection()
    {
        var team = Guid.NewGuid();
        var visibleChallenge = Guid.NewGuid();
        var deletedChallenge = Guid.NewGuid();
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(team, "alpha", false, false)],
            [
                new(Guid.NewGuid(), team, visibleChallenge, SubmissionKind.Flag, DateTimeOffset.UtcNow,
                    new ScoringEvent { Result = ScoringResult.Correct, OccurredAt = DateTimeOffset.UtcNow }),
                new(Guid.NewGuid(), team, deletedChallenge, SubmissionKind.Flag, DateTimeOffset.UtcNow,
                    new ScoringEvent { Result = ScoringResult.Correct, OccurredAt = DateTimeOffset.UtcNow })
            ],
            [],
            [new(visibleChallenge, "web", false), new(deletedChallenge, "old", true)]);

        var result = new CtfLeaderboardProjector().Project(input);

        await Assert.That(result[0].Score).IsEqualTo(500L);
        await Assert.That(result[0].Challenges[0].Direction).IsEqualTo("web");
    }

    [Test]
    public async Task CtfProjector_AppliesDecayMinimumAndFirstBloodInStableOrder()
    {
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new CtfConfiguration(
            1,
            new(500, 100, 0.5m),
            [new(BloodRewardPolicy.FixedPoints, 50)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var result = new CtfLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(firstTeam, "first", false, false), new(secondTeam, "second", false, false)],
            [
                Fact(firstTeam, challenge, 1),
                Fact(secondTeam, challenge, 2)
            ],
            [],
            [new(challenge, "web", false)],
            json));

        await Assert.That(result[0].TeamId).IsEqualTo(firstTeam);
        await Assert.That(result[0].Score).IsEqualTo(550L);
        await Assert.That(result[1].Score).IsEqualTo(250L);
    }

    [Test]
    public async Task AllModesHaveProjector()
    {
        var catalog = new LeaderboardProjectorCatalog();
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var projector = catalog.Get(mode);
            await Assert.That(projector.Mode).IsEqualTo(mode);
        }
    }

    private static LeaderboardSubmissionFact Fact(Guid team, int seconds, ScoringResult result) =>
        new(Guid.NewGuid(), team, Guid.NewGuid(), SubmissionKind.Flag, DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            new ScoringEvent { Id = Guid.NewGuid(), TeamId = team, Result = result, OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds) });

    private static LeaderboardSubmissionFact Fact(Guid team, Guid challenge, int seconds) =>
        new(Guid.NewGuid(), team, challenge, SubmissionKind.Flag, DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            new ScoringEvent { Id = Guid.NewGuid(), TeamId = team, Result = ScoringResult.Correct, OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds) });
}
