using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfSubmissionEvaluatorTests
{
    [Test]
    public async Task Hint_unlock_does_not_turn_a_correct_rejudge_into_a_duplicate()
    {
        const string flag = "flag{rejudge}";
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var receivedAt = DateTimeOffset.UtcNow;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(flag));
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Kind = SubmissionKind.Flag,
            SubmittedFlag = flag,
            SubmittedFlagSha256 = hash,
            ReceivedAt = receivedAt
        };
        var hintUnlock = new ScoringEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Kind = ScoringEventKind.HintUnlock,
            Result = ScoringResult.Correct,
            OccurredAt = receivedAt,
            CreatedAt = receivedAt
        };
        var challengeFlag = new ChallengeFlag
        {
            Id = Guid.NewGuid(),
            CompetitionChallengeId = challengeId,
            TeamId = teamId,
            Flag = flag,
            FlagSha256 = hash,
            CreatedAt = receivedAt
        };
        var context = new SubmissionProcessingContext(
            submission,
            [hintUnlock],
            [challengeFlag],
            null,
            "{}",
            "{}");

        var result = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }
}
