using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpLeaderboardProjectorTests
{
    [Test]
    public async Task Correct_break_uses_break_achievement_and_flag_is_ignored()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var configuration = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            new(AchievementSettlement.PerRound, 75),
            new(AchievementSettlement.PerRound, 25)));
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [new(teamId, "red", false, false)],
            [
                Submission(teamId, challengeId, SubmissionKind.Break, now),
                Submission(teamId, challengeId, SubmissionKind.Flag, now.AddSeconds(1))
            ],
            [],
            [new(challengeId, "Web", false)],
            configuration,
            now.AddMinutes(-1));

        var row = new AwdpLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(75);
        await Assert.That(row.SolveCount).IsEqualTo(1);
    }

    [Test]
    public async Task Fix_achievement_is_suppressed_without_current_correct_break()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var row = Project(
            teamId,
            challengeId,
            now,
            [
                Submission(
                    teamId,
                    challengeId,
                    SubmissionKind.Break,
                    now,
                    ScoringResult.Wrong),
                Submission(
                    teamId,
                    challengeId,
                    SubmissionKind.Fix,
                    now.AddSeconds(1),
                    ScoringResult.Correct)
            ]);

        await Assert.That(row.Score).IsEqualTo(0);
        await Assert.That(row.SolveCount).IsEqualTo(0);
    }

    [Test]
    public async Task Fix_achievement_returns_when_break_is_correct_again()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var row = Project(
            teamId,
            challengeId,
            now,
            [
                Submission(
                    teamId,
                    challengeId,
                    SubmissionKind.Break,
                    now,
                    ScoringResult.Correct),
                Submission(
                    teamId,
                    challengeId,
                    SubmissionKind.Fix,
                    now.AddSeconds(1),
                    ScoringResult.Correct)
            ]);

        await Assert.That(row.Score).IsEqualTo(100);
        await Assert.That(row.SolveCount).IsEqualTo(2);
    }

    [Test]
    public async Task Challenge_override_can_allow_fix_achievement_without_break()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var row = Project(
            teamId,
            challengeId,
            now,
            [
                Submission(
                    teamId,
                    challengeId,
                    SubmissionKind.Fix,
                    now,
                    ScoringResult.Correct)
            ],
            requireBreakBeforeFix: false);

        await Assert.That(row.Score).IsEqualTo(25);
        await Assert.That(row.SolveCount).IsEqualTo(1);
    }

    [Test]
    public async Task Penalties_use_their_configured_business_outcomes()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            new(AchievementSettlement.Milestone, 0),
            new(AchievementSettlement.Milestone, 0),
            ViolationPenalty: 7,
            ServiceDownPenalty: 11,
            RequireBreakBeforeFix: false,
            BreakWrongPenalty: 3,
            FixFailurePenalty: 5), options);
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [new(teamId, "red", false, false)],
            [
                Submission(teamId, challengeId, SubmissionKind.Break, now,
                    ScoringResult.Wrong, ScoringFailureCode.FlagExpired),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(1),
                    ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(2),
                    ScoringResult.Wrong, ScoringFailureCode.AwdpPatchFailed),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(3),
                    ScoringResult.Wrong, ScoringFailureCode.AwdpPatchTimeout),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(4),
                    ScoringResult.Rejected, ScoringFailureCode.AwdpViolation),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(5),
                    ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown),
                Submission(teamId, challengeId, SubmissionKind.Fix, now.AddSeconds(6),
                    ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError)
            ],
            [],
            [new(challengeId, "Web", false)],
            competition,
            now);

        var row = new AwdpLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(-36);
    }

    [Test]
    public async Task Equal_scores_rank_more_fix_achievements_before_break_achievements()
    {
        var moreFixes = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var moreBreaks = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var firstChallenge = Guid.NewGuid();
        var secondChallenge = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            new(AchievementSettlement.Milestone, 0),
            new(AchievementSettlement.Milestone, 0),
            RequireBreakBeforeFix: false), options);
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [
                new(moreFixes, "zeta", false, false),
                new(moreBreaks, "alpha", false, false)
            ],
            [
                Submission(moreFixes, firstChallenge, SubmissionKind.Fix, now),
                Submission(moreFixes, secondChallenge, SubmissionKind.Fix, now),
                Submission(moreBreaks, firstChallenge, SubmissionKind.Fix, now),
                Submission(moreBreaks, secondChallenge, SubmissionKind.Break, now)
            ],
            [],
            [
                new(firstChallenge, "Web", false),
                new(secondChallenge, "Pwn", false)
            ],
            competition,
            now);

        var rows = new AwdpLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(moreFixes);
        await Assert.That(rows[1].TeamId).IsEqualTo(moreBreaks);
    }

    [Test]
    public async Task Remaining_ties_use_registration_time_then_team_id()
    {
        var lowerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var higherId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var later = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var registeredAt = DateTimeOffset.UtcNow;
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [
                new(later, "alpha", false, false, registeredAt.AddSeconds(1)),
                new(higherId, "beta", false, false, registeredAt),
                new(lowerId, "zeta", false, false, registeredAt)
            ],
            [],
            []);

        var rows = new AwdpLeaderboardProjector().Project(input);

        await Assert.That(rows[0].TeamId).IsEqualTo(lowerId);
        await Assert.That(rows[1].TeamId).IsEqualTo(higherId);
        await Assert.That(rows[2].TeamId).IsEqualTo(later);
    }

    [Test]
    public async Task Paused_time_does_not_split_per_round_achievements()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            new(AchievementSettlement.PerRound, 50),
            new(AchievementSettlement.PerRound, 0),
            RequireBreakBeforeFix: false), options);
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [new(teamId, "red", false, false)],
            [
                Submission(teamId, challengeId, SubmissionKind.Break, start.AddMinutes(14.5)),
                Submission(teamId, challengeId, SubmissionKind.Break, start.AddMinutes(15.5))
            ],
            [],
            [new(challengeId, "Web", false)],
            competition,
            start,
            [
                Lifecycle(CompetitionStatus.Published, CompetitionStatus.Running, start),
                Lifecycle(CompetitionStatus.Running, CompetitionStatus.Paused, start.AddMinutes(2)),
                Lifecycle(CompetitionStatus.Paused, CompetitionStatus.Running, start.AddMinutes(10))
            ]);

        var row = new AwdpLeaderboardProjector().Project(input).Single();

        await Assert.That(row.Score).IsEqualTo(50);
        await Assert.That(row.SolveCount).IsEqualTo(1);
    }

    private static LeaderboardEntry Project(
        Guid teamId,
        Guid challengeId,
        DateTimeOffset now,
        IReadOnlyList<LeaderboardSubmissionFact> submissions,
        bool? requireBreakBeforeFix = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = JsonSerializer.Serialize(new AwdpConfiguration(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            new(AchievementSettlement.PerRound, 75),
            new(AchievementSettlement.PerRound, 25),
            RequireBreakBeforeFix: true), options);
        var challenge = JsonSerializer.Serialize(new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            null,
            null,
            requireBreakBeforeFix,
            10,
            10), options);
        var input = new LeaderboardProjectionInput(
            Guid.NewGuid(),
            GameMode.Awdp,
            [new(teamId, "red", false, false)],
            submissions,
            [],
            [new(challengeId, "Web", false, challenge)],
            competition,
            now.AddMinutes(-1));
        return new AwdpLeaderboardProjector().Project(input).Single();
    }

    private static LeaderboardSubmissionFact Submission(
        Guid teamId,
        Guid challengeId,
        SubmissionKind kind,
        DateTimeOffset at,
        ScoringResult result = ScoringResult.Correct,
        ScoringFailureCode? failureCode = null) =>
        new(
            Guid.NewGuid(),
            teamId,
            challengeId,
            kind,
            at,
            new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = Guid.NewGuid(),
                TeamId = teamId,
                CompetitionChallengeId = challengeId,
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = result,
                FailureCode = failureCode,
                OccurredAt = at,
                CreatedAt = at
            });

    private static CompetitionLifecycleAudit Lifecycle(
        CompetitionStatus from,
        CompetitionStatus to,
        DateTimeOffset at) => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            From = from,
            To = to,
            OccurredAt = at
        };
}
