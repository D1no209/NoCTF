using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpSubmissionEvaluatorTests
{
    [Test]
    public async Task Break_with_foreign_team_flag_creates_rejected_evidence()
    {
        const string flag = "flag{awdp-foreign}";
        var ownerTeamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = SubmissionKind.Break,
            SubmittedFlag = flag,
            SubmittedFlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            ReceivedAt = receivedAt
        };
        var context = new SubmissionProcessingContext(
            submission,
            [],
            [new ChallengeFlag
            {
                Id = Guid.NewGuid(),
                CompetitionChallengeId = submission.CompetitionChallengeId,
                TeamId = ownerTeamId,
                Flag = flag,
                FlagSha256 = submission.SubmittedFlagSha256,
                CreatedAt = receivedAt
            }],
            null,
            "{}",
            "{}");

        var decision = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(decision.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(decision.FailureCode)
            .IsEqualTo(ScoringFailureCode.ForeignTeamFlagDetected);
        await Assert.That(decision.VictimTeamId).IsEqualTo(ownerTeamId);
    }

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
