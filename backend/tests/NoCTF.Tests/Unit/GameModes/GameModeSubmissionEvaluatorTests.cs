using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Submission;
using NoCTF.GameModes.Penetration.Configuration;
using System.Text.Json;

namespace NoCTF.Tests.Unit.GameModes;

public class GameModeSubmissionEvaluatorTests
{
    [Test]
    public async Task Catalog_ProvidesEvaluatorForEveryMode()
    {
        var catalog = new GameModeSubmissionEvaluatorCatalog();
        foreach (var mode in Enum.GetValues<GameMode>())
            await Assert.That(catalog.Get(mode)).IsNotNull();
    }

    [Test]
    public async Task KohEvaluator_RejectsFlagAndFixSubmissions()
    {
        var evaluator = new KohSubmissionEvaluator();
        var flag = new Submission { Kind = SubmissionKind.Flag, ReceivedAt = DateTimeOffset.UtcNow };
        var fix = new Submission { Kind = SubmissionKind.Fix, ReceivedAt = DateTimeOffset.UtcNow };

        var flagResult = evaluator.Evaluate(new(flag, [], [], null, "{}", "{}"));
        var fixResult = evaluator.Evaluate(new(fix, [], [], null, "{}", "{}"));

        await Assert.That(flagResult.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(flagResult.FailureCode).IsEqualTo(ScoringFailureCode.FlagNotSupported);
        await Assert.That(fixResult.FailureCode).IsEqualTo(ScoringFailureCode.FixNotSupported);
    }

    [Test]
    public async Task CtfEvaluator_RejectsFixButKeepsFlagEvaluation()
    {
        var evaluator = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator());
        var submission = new Submission { Kind = SubmissionKind.Fix, ReceivedAt = DateTimeOffset.UtcNow };

        var result = evaluator.Evaluate(new(submission, [], [], null, "{}", "{}"));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.FixNotSupported);
    }

    [Test]
    public async Task AwdEvaluator_RejectsSelfAttack()
    {
        var teamId = Guid.NewGuid();
        var submission = new Submission
        {
            TeamId = teamId,
            VictimTeamId = teamId,
            Kind = SubmissionKind.Flag,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var result = new AwdSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [], [], null, "{}", "{}"));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.SelfAttackRejected);
    }

    [Test]
    public async Task AwdEvaluator_DetectsDuplicateAttackDimensions()
    {
        var teamId = Guid.NewGuid();
        var victimId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var prior = new Submission
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            VictimTeamId = victimId,
            ServiceId = serviceId,
            Kind = SubmissionKind.Flag
        };
        var priorEvent = new ScoringEvent { SubmissionId = prior.Id, Result = ScoringResult.Correct };
        var submission = new Submission
        {
            TeamId = teamId,
            VictimTeamId = victimId,
            ServiceId = serviceId,
            Kind = SubmissionKind.Flag,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var result = new AwdSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [priorEvent], [], null, "{}", "{}", [prior]));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Duplicate);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.DuplicateAttack);
    }

    [Test]
    public async Task AwdpEvaluator_RequiresBreakBeforeFix()
    {
        var submission = new Submission
        {
            TeamId = Guid.NewGuid(),
            ChallengeId = Guid.NewGuid(),
            Kind = SubmissionKind.Fix,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var result = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [], [], null, "{}", """{"schemaVersion":1,"requireBreakBeforeFix":true,"maxBreakAttempts":10,"maxFixAttempts":10}"""));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.BreakRequired);
    }

    [Test]
    public async Task AwdpEvaluator_PreventsSecondSuccessfulFix()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var prior = new Submission { Id = Guid.NewGuid(), TeamId = teamId, ChallengeId = challengeId, Kind = SubmissionKind.Fix };
        var priorEvent = new ScoringEvent { SubmissionId = prior.Id, Result = ScoringResult.Correct };
        var submission = new Submission
        {
            TeamId = teamId,
            ChallengeId = challengeId,
            Kind = SubmissionKind.Fix,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var result = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [priorEvent], [], null, "{}", """{"schemaVersion":1,"requireBreakBeforeFix":false,"maxBreakAttempts":10,"maxFixAttempts":10}""", [prior]));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Duplicate);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.AchievementAlreadyCompleted);
    }

    [Test]
    public async Task PenetrationEvaluator_RequiresCompletedPrerequisites()
    {
        var firstStage = Guid.NewGuid();
        var secondStage = Guid.NewGuid();
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1,
            [
                new(firstStage, 1, "entry", [], null),
                new(secondStage, 2, "root", [firstStage], null)
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var submission = new Submission
        {
            TeamId = team,
            ChallengeId = challenge,
            StageId = secondStage,
            Kind = SubmissionKind.Flag,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var result = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [], [], null, "{}", configuration));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.StagePrerequisiteIncomplete);
    }

    [Test]
    public async Task PenetrationEvaluator_AllowsStageAfterPrerequisite()
    {
        var firstStage = Guid.NewGuid();
        var secondStage = Guid.NewGuid();
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var prior = new Submission { Id = Guid.NewGuid(), TeamId = team, ChallengeId = challenge, StageId = firstStage, Kind = SubmissionKind.Flag };
        var configuration = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1,
            [new(firstStage, 1, "entry", [], null), new(secondStage, 2, "root", [firstStage], null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var submission = new Submission
        {
            TeamId = team, ChallengeId = challenge, StageId = secondStage, Kind = SubmissionKind.Flag,
            Flag = "flag", ReceivedAt = DateTimeOffset.UtcNow
        };
        var flag = new NoCTF.Domain.Challenges.ChallengeFlag { TeamId = team, ChallengeId = challenge, Flag = "flag" };

        var result = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [new ScoringEvent { SubmissionId = prior.Id, Result = ScoringResult.Correct }], [flag], null, "{}", configuration, [prior]));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }
}
