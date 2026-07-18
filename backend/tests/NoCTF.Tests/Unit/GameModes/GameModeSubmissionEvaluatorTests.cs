using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Submission;
using NoCTF.GameModes.Ctf.Submission;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class GameModeSubmissionEvaluatorTests
{
    [Test]
    public async Task Ctf_allows_each_team_to_solve_the_same_challenge()
    {
        var challengeId = Guid.NewGuid();
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var first = new FlagSubmissionReceived
        {
            SubmissionId = Guid.NewGuid(), CompetitionId = Guid.NewGuid(), TeamId = firstTeam,
            ChallengeId = challengeId, UserId = Guid.NewGuid(), Flag = "flag{ok}", IpAddress = "127.0.0.1",
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var context = Context(first.CompetitionId, GameMode.Ctf, challengeId, firstTeam,
            [new(1, new FlagSubmissionEvaluated(first.SubmissionId, first.CompetitionId, firstTeam,
                challengeId, SubmissionOutcome.Correct, first.ReceivedAt), first.ReceivedAt)]);

        var second = new FlagSubmissionReceived
        {
            SubmissionId = Guid.NewGuid(), CompetitionId = first.CompetitionId, TeamId = secondTeam,
            ChallengeId = challengeId, UserId = Guid.NewGuid(), Flag = "flag{ok}", IpAddress = "127.0.0.1",
            ReceivedAt = first.ReceivedAt.AddSeconds(1)
        };
        var result = new CtfSubmissionEvaluator().EvaluateFlag(second, context with { CurrentSubmissionSequence = 2 });

        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.Correct);
    }

    [Test]
    public async Task Awdp_platform_failure_does_not_consume_fix_attempt()
    {
        var submission = FixSubmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var context = Context(submission.CompetitionId, GameMode.Awdp, submission.ChallengeId, submission.TeamId, []) with
        {
            ChallengeConfigurationJson = """{"schemaVersion":1,"break":null,"fix":{"settlement":0,"points":10},"requireBreakBeforeFix":false,"maxBreakAttempts":1,"maxFixAttempts":1}""",
            Archive = new(ArchiveValidationStatus.PlatformFailed, SubmissionErrorCode.StorageUnavailable)
        };

        var result = new AwdpSubmissionEvaluator().EvaluateFix(submission, context);

        await Assert.That(result.Outcome).IsEqualTo(SubmissionOutcome.PlatformFailed);
        await Assert.That(result.ConsumedAttempt).IsFalse();
    }

    private static SubmissionEvaluationContext Context(
        Guid competitionId,
        GameMode mode,
        Guid challengeId,
        Guid teamId,
        IReadOnlyList<SubmissionHistoryItem> history) => new(
            competitionId,
            mode,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            60,
            """{"schemaVersion":1}""",
            """{"schemaVersion":1,"flag":"flag{ok}","break":null,"fix":null,"requireBreakBeforeFix":false,"maxBreakAttempts":1,"maxFixAttempts":1}""",
            history,
            history.Count + 1,
            "flag{ok}",
            [],
            null);

    private static FixSubmissionReceived FixSubmission(Guid competitionId, Guid teamId, Guid challengeId) =>
        new(Guid.NewGuid(), competitionId, teamId, challengeId, Guid.NewGuid(), Guid.NewGuid(),
            new("fix/a.zip", "a.zip", "application/zip", 1, new string('a', 64)), "127.0.0.1", DateTimeOffset.UtcNow);
}
