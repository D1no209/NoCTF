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

    private static LeaderboardSubmissionFact Submission(
        Guid teamId,
        Guid challengeId,
        SubmissionKind kind,
        DateTimeOffset at) =>
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
                Result = ScoringResult.Correct,
                OccurredAt = at,
                CreatedAt = at
            });
}
