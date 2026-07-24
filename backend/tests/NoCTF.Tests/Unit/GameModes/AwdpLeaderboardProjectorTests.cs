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
        ScoringResult result = ScoringResult.Correct) =>
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
                OccurredAt = at,
                CreatedAt = at
            });
}
