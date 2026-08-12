using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpGameplayFactEvaluatorTests
{
    [Test]
    public async Task Break_does_not_accept_regular_expression_flags()
    {
        const string flag = "flag{awdp-regex}";
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.BreakAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = receivedAt
        };
        var expression = new ChallengeFlag
        {
            Id = Guid.NewGuid(),
            CompetitionChallengeId = submission.CompetitionChallengeId,
            Flag = @"flag\{.*\}",
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(@"flag\{.*\}")),
            MatchKind = ChallengeFlagMatchKind.RegularExpression,
            CreatedAt = receivedAt
        };

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(new(submission, [], [expression], null, "{}", "{}"));

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Wrong);
    }

    [Test]
    [Arguments(AchievementSettlement.Milestone)]
    [Arguments(AchievementSettlement.PerRound)]
    public async Task Break_after_prior_correct_remains_correct(
        AchievementSettlement settlement)
    {
        const string flag = "flag{awdp-repeat}";
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.BreakAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = receivedAt
        };
        var priorFact = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = submission.CompetitionId,
            CompetitionChallengeId = submission.CompetitionChallengeId,
            TeamId = submission.TeamId,
            Kind = GameplayFactKind.BreakAttempt,
            OccurredAt = receivedAt.AddMinutes(-10),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [priorFact],
            [new ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = submission.TeamId,
                Flag = flag,
                FlagSha256 = submission.ValueSha256,
                CreatedAt = receivedAt
            }],
            null,
            JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                300,
                new(settlement, 100),
                new(AchievementSettlement.Milestone, 50))),
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(decision.FailureCode).IsNull();
    }

    [Test]
    [Arguments(AchievementSettlement.Milestone, 100L, 1)]
    [Arguments(AchievementSettlement.PerRound, 200L, 2)]
    public async Task Projection_deduplicates_unsorted_breaks_by_configured_settlement(
        AchievementSettlement settlement,
        long expectedScore,
        int expectedSolveCount)
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var facts = new[]
        {
            Fact(startedAt.AddSeconds(70)),
            Fact(startedAt.AddSeconds(20)),
            Fact(startedAt.AddSeconds(10))
        };
        var configuration = JsonSerializer.Serialize(
            new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                60,
                new(settlement, 100),
                new(AchievementSettlement.Milestone, 50)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var projection = new AwdpLeaderboardProjector().Project(new(
            competitionId,
            GameMode.Awdp,
            [new LeaderboardTeamFact(teamId, "Team", false, false, startedAt)],
            facts,
            [new LeaderboardChallengeFact(challengeId, "Web", "Challenge", false, "{}")],
            configuration,
            startedAt));

        var entry = projection.Entries.Single();
        await Assert.That(entry.Score).IsEqualTo(expectedScore);
        await Assert.That(entry.SolveCount).IsEqualTo(expectedSolveCount);
        await Assert.That(projection.Cells.Single().Score).IsEqualTo(expectedScore);

        LeaderboardGameplayFact Fact(DateTimeOffset occurredAt) => new(
            Guid.CreateVersion7(occurredAt),
            teamId,
            challengeId,
            GameplayFactKind.BreakAttempt,
            occurredAt,
            GameplayFactState.Completed,
            GameplayFactResult.Correct,
            null);
    }

    [Test]
    public async Task Break_with_foreign_team_flag_creates_rejected_evidence()
    {
        const string flag = "flag{awdp-foreign}";
        var ownerTeamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.BreakAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = receivedAt
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [],
            [new ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = ownerTeamId,
                Flag = flag,
                FlagSha256 = submission.ValueSha256,
                CreatedAt = receivedAt
            }],
            null,
            "{}",
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
        await Assert.That(decision.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Accepted_fix_after_prior_correct_fix_still_requires_runner()
    {
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.FixAttempt,
            OccurredAt = DateTimeOffset.UtcNow
        };
        var priorFact = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = submission.CompetitionId,
            CompetitionChallengeId = submission.CompetitionChallengeId,
            TeamId = submission.TeamId,
            Kind = GameplayFactKind.FixAttempt,
            OccurredAt = submission.OccurredAt.AddSeconds(-1),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [priorFact],
            [],
            null,
            "{}",
            JsonSerializer.Serialize(
                new AwdpChallengeConfiguration(
                    AwdpChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    new AwdpAchievementConfiguration(AchievementSettlement.Milestone, 20),
                    true,
                    10,
                    10),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsNull();
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.CheckerPlatformError);
    }
}
