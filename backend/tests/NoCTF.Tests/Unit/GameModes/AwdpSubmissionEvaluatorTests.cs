using System.Text.Json;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpSubmissionEvaluatorTests
{
    [Test]
    public async Task Accepted_fix_reprocessing_does_not_repeat_break_admission()
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
        var context = new SubmissionProcessingContext(
            submission,
            [],
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

        var decision = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(ScoringResult.PlatformFailed);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(ScoringFailureCode.CheckerPlatformError);
    }
}
