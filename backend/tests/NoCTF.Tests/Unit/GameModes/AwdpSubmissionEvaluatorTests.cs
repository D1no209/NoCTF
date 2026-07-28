using System.Text.Json;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpSubmissionEvaluatorTests
{
    [Test]
    public async Task Accepted_fix_after_prior_correct_fix_still_requires_runner()
    {
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = SubmissionKind.Fix,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var priorSubmission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = submission.CompetitionId,
            CompetitionChallengeId = submission.CompetitionChallengeId,
            TeamId = submission.TeamId,
            Kind = SubmissionKind.Fix,
            ReceivedAt = submission.ReceivedAt.AddSeconds(-1)
        };
        var context = new SubmissionProcessingContext(
            submission,
            [new ScoringEvent
            {
                Id = Guid.NewGuid(),
                CompetitionId = submission.CompetitionId,
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = submission.TeamId,
                SubmissionId = priorSubmission.Id,
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Correct,
                OccurredAt = priorSubmission.ReceivedAt,
                CreatedAt = priorSubmission.ReceivedAt
            }],
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
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            [priorSubmission]);

        var decision = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(ScoringResult.PlatformFailed);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(ScoringFailureCode.CheckerPlatformError);
    }
}
