using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Penetration.Configuration;
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
        var system = new ScoringEvent
        {
            Id = Guid.NewGuid(),
            TeamId = team,
            Kind = ScoringEventKind.AwdServiceCheck,
            Result = ScoringResult.Correct,
            OccurredAt = DateTimeOffset.UtcNow
        };
        var input = new LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Awd,
            [new(team, "alpha", false, false)], [], [new(system)]);

        var result = new AwdLeaderboardProjector().Project(input);

        await Assert.That(result[0].Score).IsEqualTo(100L);
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
            Kind = ScoringEventKind.KohObservation,
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
            Kind = ScoringEventKind.KohObservation,
            Result = ScoringResult.Correct,
            OccurredAt = DateTimeOffset.UtcNow
        };

        var result = new KohLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Koh,
            [new(team, "alpha", false, false)],
            [Fact(team, 1, ScoringResult.Correct)],
            [new(system)]));

        await Assert.That(result[0].Score).IsEqualTo(10L);
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
    public async Task AwdProjector_AwardsAttackAndVictimPenaltyAndServiceFacts()
    {
        var attacker = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(
            new AwdConfiguration(1, 300, 10, 2, 50, 100, 30, 20),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var submission = Fact(attacker, Guid.NewGuid(), 1) with { VictimTeamId = victim };
        var service = new ScoringEvent
        {
            Id = Guid.NewGuid(),
            TeamId = attacker,
            Kind = ScoringEventKind.AwdServiceCheck,
            Result = ScoringResult.Correct,
            OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(2)
        };

        var result = new AwdLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awd,
            [new(attacker, "attacker", false, false), new(victim, "victim", false, false)],
            [submission],
            [new(service)],
            [],
            configuration));

        await Assert.That(result.Single(item => item.TeamId == attacker).Score).IsEqualTo(150L);
        await Assert.That(result.Single(item => item.TeamId == victim).Score).IsEqualTo(-20L);
    }

    [Test]
    public async Task AwdpProjector_AppliesMilestoneAndPerRoundSettlement()
    {
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var start = DateTimeOffset.UnixEpoch;
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            1,
            60,
            new(AchievementSettlement.Milestone, 100),
            new(AchievementSettlement.PerRound, 50)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var facts = new[]
        {
            Achievement(team, challenge, SubmissionKind.Flag, start.AddSeconds(1)),
            Achievement(team, challenge, SubmissionKind.Flag, start.AddSeconds(61)),
            Achievement(team, challenge, SubmissionKind.Fix, start.AddSeconds(2)),
            Achievement(team, challenge, SubmissionKind.Fix, start.AddSeconds(62))
        };

        var result = new AwdpLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(), GameMode.Awdp, [new(team, "team", false, false)], facts, [],
            [new(challenge, "pwn", false)], configuration, start));

        await Assert.That(result[0].Score).IsEqualTo(200L);
        await Assert.That(result[0].SolveCount).IsEqualTo(3);
    }

    [Test]
    public async Task AwdpProjector_AppliesViolationAndServicePenaltiesButIgnoresPlatformFailures()
    {
        var team = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            2, 60,
            new(AchievementSettlement.Milestone, 100),
            new(AchievementSettlement.Milestone, 50),
            30,
            20), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var systemFacts = new[]
        {
            AwdpCheck(team, ScoringResult.Rejected, ScoringFailureCode.AwdpViolation),
            AwdpCheck(team, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown),
            AwdpCheck(team, ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError),
            AwdpCheck(team, ScoringResult.Correct, ScoringFailureCode.AwdpViolation),
            AwdpCheck(team, ScoringResult.Rejected, ScoringFailureCode.AwdpServiceDown)
        };

        var result = new AwdpLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(), GameMode.Awdp, [new(team, "team", false, false)], [], systemFacts,
            [], configuration, DateTimeOffset.UnixEpoch));

        await Assert.That(result[0].Score).IsEqualTo(-50L);
    }

    [Test]
    public async Task KohProjector_AwardsEachIntervalAcrossControlTransitions()
    {
        var controller = Guid.NewGuid();
        var other = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(new KohConfiguration(1, 5, 25),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var observations = new[]
        {
            Observation(controller, 1),
            Observation(other, 2),
            Observation(controller, 3)
        };

        var result = new KohLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(), GameMode.Koh,
            [new(controller, "controller", false, false), new(other, "other", false, false)],
            [], observations, [], configuration));

        await Assert.That(result.Single(item => item.TeamId == controller).Score).IsEqualTo(50L);
        await Assert.That(result.Single(item => item.TeamId == other).Score).IsEqualTo(25L);
        await Assert.That(result.Single(item => item.TeamId == controller).LastScoreAt)
            .IsEqualTo(DateTimeOffset.UnixEpoch.AddSeconds(3));
        await Assert.That(result.Single(item => item.TeamId == other).LastScoreAt)
            .IsEqualTo(DateTimeOffset.UnixEpoch.AddSeconds(2));
    }

    [Test]
    public async Task PenetrationProjector_AwardsDistinctStagesWithoutCompletingWholeChallengeEarly()
    {
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var firstStage = Guid.NewGuid();
        var secondStage = Guid.NewGuid();
        var competition = JsonSerializer.Serialize(new PenetrationConfiguration(1, new(500, 100, 0.5m), []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var challengeConfiguration = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1,
            [new(firstStage, 1, "entry", [], new(200, 100, 0.5m)), new(secondStage, 2, "root", [firstStage], new(300, 100, 0.5m))]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var facts = new[]
        {
            Stage(team, challenge, firstStage, 1),
            Stage(team, challenge, firstStage, 2),
            Stage(team, challenge, secondStage, 3)
        };

        var result = new PenetrationLeaderboardProjector().Project(new LeaderboardProjectionInput(
            Guid.NewGuid(), GameMode.Penetration, [new(team, "team", false, false)], facts, [],
            [new(challenge, "pentest", false, challengeConfiguration)], competition));

        await Assert.That(result[0].Score).IsEqualTo(500L);
        await Assert.That(result[0].SolveCount).IsEqualTo(2);
        await Assert.That(result[0].Challenges[0].SolveCount).IsEqualTo(2);
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

    private static LeaderboardSubmissionFact Achievement(Guid team, Guid challenge, SubmissionKind kind, DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), team, challenge, kind, occurredAt,
            new ScoringEvent { Id = Guid.NewGuid(), TeamId = team, Result = ScoringResult.Correct, OccurredAt = occurredAt });

    private static LeaderboardSystemFact Observation(Guid team, int seconds) => new(new ScoringEvent
    {
        Id = Guid.NewGuid(),
        TeamId = team,
        Kind = ScoringEventKind.KohObservation,
        Result = ScoringResult.Correct,
        OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds)
    });

    private static LeaderboardSystemFact AwdpCheck(
        Guid team,
        ScoringResult result,
        ScoringFailureCode failureCode) => new(new ScoringEvent
    {
        Id = Guid.NewGuid(),
        TeamId = team,
        Kind = ScoringEventKind.AwdpFixCheck,
        Result = result,
        FailureCode = failureCode,
        OccurredAt = DateTimeOffset.UnixEpoch
    });

    private static LeaderboardSubmissionFact Stage(Guid team, Guid challenge, Guid stage, int seconds) =>
        new(Guid.NewGuid(), team, challenge, SubmissionKind.Flag, DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            new ScoringEvent
            {
                Id = Guid.NewGuid(), TeamId = team, ChallengeId = challenge, Result = ScoringResult.Correct,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds)
            }, StageId: stage);
}
