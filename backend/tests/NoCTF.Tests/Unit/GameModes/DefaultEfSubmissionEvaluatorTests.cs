using NoCTF.Domain.Challenges;
using NoCTF.Domain.Submissions;
using NoCTF.Application.Submissions.Processing;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public class DefaultEfSubmissionEvaluatorTests
{
    [Test]
    public async Task Evaluate_CorrectFlag_ReturnsCorrectSubmissionEvent()
    {
        var received = DateTimeOffset.UtcNow;
        var fingerprint = FlagFingerprint.Create("flag");
        var submission = new Submission { Id = Guid.NewGuid(), CompetitionId = Guid.NewGuid(), TeamId = Guid.NewGuid(), CompetitionChallengeId = Guid.NewGuid(), Kind = SubmissionKind.Flag, FlagHash = fingerprint.Sha256, FlagLength = fingerprint.Length, ReceivedAt = received };
        var context = new SubmissionProcessingContext(submission, [], [new ChallengeFlag { Flag = "flag", CompetitionId = submission.CompetitionId, CompetitionChallengeId = submission.CompetitionChallengeId!.Value }], null, "{}", "{}");

        var result = new DefaultEfSubmissionEvaluator().Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
        await Assert.That(result.Kind).IsEqualTo(ScoringEventKind.SubmissionEvaluation);
    }

    [Test]
    public async Task Evaluate_PriorCorrectFact_ReturnsDuplicate()
    {
        var fingerprint = FlagFingerprint.Create("flag");
        var submission = new Submission { Id = Guid.NewGuid(), CompetitionId = Guid.NewGuid(), TeamId = Guid.NewGuid(), CompetitionChallengeId = Guid.NewGuid(), Kind = SubmissionKind.Flag, FlagHash = fingerprint.Sha256, FlagLength = fingerprint.Length, ReceivedAt = DateTimeOffset.UtcNow };
        var prior = new ScoringEvent { Id = Guid.NewGuid(), TeamId = submission.TeamId, CompetitionChallengeId = submission.CompetitionChallengeId, Result = ScoringResult.Correct };
        var context = new SubmissionProcessingContext(submission, [prior], [], null, "{}", "{}");

        var result = new DefaultEfSubmissionEvaluator().Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Duplicate);
    }

    [Test]
    public async Task Evaluate_ValidFix_ReturnsCorrect()
    {
        var submission = new Submission { Id = Guid.NewGuid(), CompetitionId = Guid.NewGuid(), TeamId = Guid.NewGuid(), CompetitionChallengeId = Guid.NewGuid(), Kind = SubmissionKind.Fix, ReceivedAt = DateTimeOffset.UtcNow };
        var fix = new FixSubmissionRecord { SubmissionId = submission.Id, VerificationStatus = NoCTF.Domain.Submissions.FixVerificationStatus.Valid };

        var result = new DefaultEfSubmissionEvaluator().Evaluate(new(submission, [], [], fix, "{}", "{}"));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
        await Assert.That(result.FailureCode).IsNull();
    }
}
