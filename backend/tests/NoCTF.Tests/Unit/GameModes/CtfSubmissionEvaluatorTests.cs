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
    public async Task Foreign_team_flag_is_rejected_with_owner_team_evidence()
    {
        var fixture = CreateFixture("flag{foreign}");
        var ownerTeamId = Guid.NewGuid();
        var context = fixture.Context([
            fixture.Flag(ownerTeamId)
        ]);

        var result = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode)
            .IsEqualTo(ScoringFailureCode.ForeignTeamFlagDetected);
        await Assert.That(result.VictimTeamId).IsEqualTo(ownerTeamId);
    }

    [Test]
    public async Task Own_or_public_flag_takes_precedence_over_foreign_match()
    {
        var fixture = CreateFixture("flag{shared}");
        var context = fixture.Context([
            fixture.Flag(Guid.NewGuid()),
            fixture.Flag(null)
        ]);

        var result = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Expired_foreign_flag_is_an_ordinary_wrong_answer()
    {
        var fixture = CreateFixture("flag{expired}");
        var expired = fixture.Flag(Guid.NewGuid());
        expired.ValidUntil = fixture.ReceivedAt;
        var context = fixture.Context([expired]);

        var result = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Wrong);
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.VictimTeamId).IsNull();
    }

    [Test]
    public async Task Shared_foreign_flag_is_rejected_without_selecting_an_owner_team()
    {
        var fixture = CreateFixture("flag{ambiguous}");
        var context = fixture.Context([
            fixture.Flag(Guid.NewGuid()),
            fixture.Flag(Guid.NewGuid())
        ]);

        var result = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(context);

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode)
            .IsEqualTo(ScoringFailureCode.AmbiguousFlagMatch);
        await Assert.That(result.VictimTeamId).IsNull();
    }

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

    private static Fixture CreateFixture(string flag)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            CompetitionChallengeId = Guid.NewGuid(),
            TeamId = Guid.NewGuid(),
            Kind = SubmissionKind.Flag,
            SubmittedFlag = flag,
            SubmittedFlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            ReceivedAt = receivedAt
        };
        return new(submission, receivedAt);
    }

    private sealed record Fixture(Submission Submission, DateTimeOffset ReceivedAt)
    {
        public SubmissionProcessingContext Context(IReadOnlyList<ChallengeFlag> flags) =>
            new(Submission, [], flags, null, "{}", "{}");

        public ChallengeFlag Flag(Guid? teamId) => new()
        {
            Id = Guid.NewGuid(),
            CompetitionChallengeId = Submission.CompetitionChallengeId,
            TeamId = teamId,
            Flag = Submission.SubmittedFlag!,
            FlagSha256 = Submission.SubmittedFlagSha256!,
            CreatedAt = ReceivedAt
        };
    }
}
