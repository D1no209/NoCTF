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
            .Evaluate(new(submission, [], [expression], null, LegacyCompetitionJson(), "{}"));

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
                SpecificationKind = SpecificationKind.RuntimeGeneration,
                SpecificationId = Guid.NewGuid(),
                ValidStart = receivedAt.AddSeconds(-1),
                CreatedAt = receivedAt
            }],
            null,
            JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                300,
                new(settlement, 100),
                new(AchievementSettlement.Milestone, 50)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(decision.FailureCode).IsNull();
    }

    [Test]
    public async Task Continuous_break_rejects_generation_flag_before_runtime_injection()
    {
        const string flag = "flag{awdp-not-injected}";
        var occurredAt = DateTimeOffset.UtcNow;
        var submission = new GameplayFact
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = GameplayFactKind.BreakAttempt,
            Value = flag,
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            OccurredAt = occurredAt
        };
        var context = new GameplayFactProcessingContext(
            submission,
            [],
            [new ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = submission.TeamId,
                Flag = flag,
                FlagSha256 = submission.ValueSha256,
                SpecificationKind = SpecificationKind.RuntimeGeneration,
                SpecificationId = Guid.NewGuid(),
                ValidStart = null,
                ValidUntil = null,
                CreatedAt = occurredAt
            }],
            null,
            JsonSerializer.Serialize(new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                300,
                new(AchievementSettlement.PerRound, 100),
                new(AchievementSettlement.PerRound, 50),
                RequireBreakBeforeFix: false),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Wrong);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.FlagExpired);
    }

    [Test]
    [Arguments(AchievementSettlement.Milestone, 100L, 1)]
    [Arguments(AchievementSettlement.PerRound, 200L, 2)]
    public async Task Legacy_projection_deduplicates_unsorted_breaks_by_configured_settlement(
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
                AwdpConfiguration.LegacySchemaVersion,
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
            LegacyCompetitionJson(),
            "{}");

        var decision = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(GameplayFactFailureCode.ForeignTeamFlagDetected);
        await Assert.That(decision.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Continuous_projection_accumulates_one_break_and_fix_activation_per_round()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var facts = new[]
        {
            Fact(GameplayFactKind.BreakAttempt, startedAt.AddSeconds(70), GameplayFactResult.Correct),
            Fact(GameplayFactKind.BreakAttempt, startedAt.AddSeconds(80), GameplayFactResult.Correct),
            Fact(GameplayFactKind.BreakAttempt, startedAt.AddSeconds(130), GameplayFactResult.Correct),
            Fact(GameplayFactKind.FixAttempt, startedAt.AddSeconds(130), GameplayFactResult.Correct),
            Fact(GameplayFactKind.FixAttempt, startedAt.AddSeconds(140), GameplayFactResult.Correct),
            Fact(GameplayFactKind.BreakAttempt, startedAt.AddSeconds(150), GameplayFactResult.Wrong)
        };
        var configuration = JsonSerializer.Serialize(
            new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                60,
                new(AchievementSettlement.PerRound, 40),
                new(AchievementSettlement.PerRound, 60),
                BreakWrongPenalty: 7),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var projection = new AwdpLeaderboardProjector().Project(new(
            competitionId,
            GameMode.Awdp,
            [new LeaderboardTeamFact(teamId, "Team", false, false, startedAt)],
            facts,
            [new LeaderboardChallengeFact(challengeId, "Pwn", "Challenge", false, "{\"schemaVersion\":2}")],
            configuration,
            startedAt,
            ProjectedAt: startedAt.AddSeconds(250)));

        var entry = projection.Entries.Single();
        await Assert.That(entry.Score).IsEqualTo(333L);
        await Assert.That(entry.SolveCount).IsEqualTo(2);
        await Assert.That(projection.Cells.Single().Score).IsEqualTo(340L);

        LeaderboardGameplayFact Fact(
            GameplayFactKind kind,
            DateTimeOffset occurredAt,
            GameplayFactResult result) => new(
                Guid.CreateVersion7(occurredAt),
                teamId,
                challengeId,
                kind,
                occurredAt,
                GameplayFactState.Completed,
                result,
                null);
    }

    [Test]
    public async Task Continuous_projection_uses_pause_aware_clock_and_freezes_after_finish()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var breakAt = startedAt.AddSeconds(30);
        var transitions = new[]
        {
            Transition(CompetitionStatus.Published, CompetitionStatus.Running, startedAt),
            Transition(CompetitionStatus.Running, CompetitionStatus.Paused, startedAt.AddSeconds(90)),
            Transition(CompetitionStatus.Paused, CompetitionStatus.Running, startedAt.AddSeconds(190)),
            Transition(CompetitionStatus.Running, CompetitionStatus.Finished, startedAt.AddSeconds(220))
        };
        var configuration = JsonSerializer.Serialize(
            new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                60,
                new(AchievementSettlement.PerRound, 25),
                new(AchievementSettlement.PerRound, 50)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var input = new LeaderboardProjectionInput(
            competitionId,
            GameMode.Awdp,
            [new LeaderboardTeamFact(teamId, "Team", false, false, startedAt)],
            [new(
                Guid.CreateVersion7(breakAt),
                teamId,
                challengeId,
                GameplayFactKind.BreakAttempt,
                breakAt,
                GameplayFactState.Completed,
                GameplayFactResult.Correct,
                null)],
            [new LeaderboardChallengeFact(challengeId, "Pwn", "Challenge", false, "{\"schemaVersion\":2}")],
            configuration,
            startedAt,
            transitions,
            ProjectedAt: startedAt.AddMinutes(30));

        var projection = new AwdpLeaderboardProjector().Project(input);

        await Assert.That(projection.Entries.Single().Score).IsEqualTo(75L);

        CompetitionLifecycleTransition Transition(
            CompetitionStatus from,
            CompetitionStatus to,
            DateTimeOffset occurredAt) => new()
            {
                Id = Guid.CreateVersion7(occurredAt),
                CompetitionId = competitionId,
                From = from,
                To = to,
                OccurredAt = occurredAt
            };
    }

    [Test]
    public async Task Continuous_projection_honors_milestones_and_break_gated_fix_rejudges()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var breakId = Guid.CreateVersion7(startedAt.AddSeconds(20));
        var fixId = Guid.CreateVersion7(startedAt.AddSeconds(30));
        var configuration = JsonSerializer.Serialize(
            new AwdpConfiguration(
                AwdpConfiguration.CurrentSchemaVersion,
                60,
                new(AchievementSettlement.PerRound, 1),
                new(AchievementSettlement.PerRound, 1)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var challengeConfiguration = JsonSerializer.Serialize(
            new AwdpChallengeConfiguration(
                AwdpChallengeConfiguration.CurrentSchemaVersion,
                new(AchievementSettlement.Milestone, 100),
                new(AchievementSettlement.PerRound, 25),
                RequireBreakBeforeFix: true,
                MaxBreakSubmissions: null,
                MaxFixSubmissions: null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        LeaderboardProjectionInput Input(GameplayFactResult breakResult) => new(
            competitionId,
            GameMode.Awdp,
            [new LeaderboardTeamFact(teamId, "Team", false, false, startedAt)],
            [
                new(breakId, teamId, challengeId, GameplayFactKind.BreakAttempt,
                    startedAt.AddSeconds(20), GameplayFactState.Completed, breakResult, null),
                new(fixId, teamId, challengeId, GameplayFactKind.FixAttempt,
                    startedAt.AddSeconds(30), GameplayFactState.Completed,
                    GameplayFactResult.Correct, null)
            ],
            [new LeaderboardChallengeFact(
                challengeId,
                "Pwn",
                "Challenge",
                false,
                challengeConfiguration)],
            configuration,
            startedAt,
            ProjectedAt: startedAt.AddSeconds(130));

        var beforeCorrection = new AwdpLeaderboardProjector().Project(
            Input(GameplayFactResult.Correct));
        var afterCorrection = new AwdpLeaderboardProjector().Project(
            Input(GameplayFactResult.Wrong));
        var restored = new AwdpLeaderboardProjector().Project(
            Input(GameplayFactResult.Correct));

        await Assert.That(beforeCorrection.Entries.Single().Score).IsEqualTo(175L);
        await Assert.That(beforeCorrection.Entries.Single().SolveCount).IsEqualTo(2);
        await Assert.That(afterCorrection.Entries.Single().Score).IsEqualTo(0L);
        await Assert.That(afterCorrection.Entries.Single().SolveCount).IsEqualTo(0);
        await Assert.That(restored.Entries.Single().Score).IsEqualTo(175L);
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

    private static string LegacyCompetitionJson() => JsonSerializer.Serialize(
        new AwdpConfiguration(
            AwdpConfiguration.LegacySchemaVersion,
            300,
            new(AchievementSettlement.Milestone, 100),
            new(AchievementSettlement.Milestone, 50)),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
