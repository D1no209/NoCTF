using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Submission;
using NoCTF.GameModes.Penetration.Configuration;
using System.Text.Json;

namespace NoCTF.Tests.Unit.GameModes;

public class GameModeSubmissionEvaluatorTests
{
    private const string AwdJson = """{"schemaVersion":1,"roundDurationSeconds":60,"totalRounds":3,"flagValidityRounds":2,"attackPoints":100,"serviceOnlinePoints":10,"serviceDownPenalty":10,"victimPenalty":50}""";

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

        var result = new AwdSubmissionEvaluator()
            .Evaluate(new(submission, [], [], null, AwdJson, "{}", CompetitionStartTime: submission.ReceivedAt));

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

        var result = new AwdSubmissionEvaluator()
            .Evaluate(new(submission, [priorEvent], [], null, AwdJson, "{}", [prior], submission.ReceivedAt));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Duplicate);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.DuplicateAttack);
    }

    [Test]
    public async Task AwdEvaluator_AcceptsTargetFlagWithinConfiguredRoundWindow()
    {
        var start = DateTimeOffset.UtcNow;
        var target = Guid.NewGuid();
        var submission = Attack(target, start.AddSeconds(119));
        var flag = RoundFlag(target, start, "FLAG{round-1}");

        var result = new AwdSubmissionEvaluator().Evaluate(new(
            submission, [], [flag], null, AwdJson, "{}", [], start));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }

    [Test]
    public async Task AwdEvaluator_RejectsExpiredRoundFlag()
    {
        var start = DateTimeOffset.UtcNow;
        var target = Guid.NewGuid();
        var submission = Attack(target, start.AddSeconds(120));
        var flag = RoundFlag(target, start, "FLAG{round-1}");

        var result = new AwdSubmissionEvaluator().Evaluate(new(
            submission, [], [flag], null, AwdJson, "{}", [], start));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Wrong);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.FlagExpired);
    }

    [Test]
    public async Task AwdEvaluator_UsesAnyValidMatchingFlagRegardlessOfCandidateOrder()
    {
        var start = DateTimeOffset.UtcNow;
        var target = Guid.NewGuid();
        var submission = Attack(target, start.AddSeconds(70));
        var expiredGlobal = RoundFlag(Guid.Empty, start.AddSeconds(-120), "FLAG{round-1}");
        expiredGlobal.TeamId = null;
        var validTarget = RoundFlag(target, start.AddSeconds(60), "FLAG{round-1}");

        var result = new AwdSubmissionEvaluator().Evaluate(new(
            submission, [], [expiredGlobal, validTarget], null, AwdJson, "{}", [], start));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }

    [Test]
    public async Task AwdEvaluator_RejectsSubmissionOutsideConfiguredRounds()
    {
        var start = DateTimeOffset.UtcNow;
        var target = Guid.NewGuid();
        var submission = Attack(target, start.AddSeconds(180));

        var result = new AwdSubmissionEvaluator().Evaluate(new(
            submission, [], [], null, AwdJson, "{}", [], start));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(result.FailureCode).IsEqualTo(ScoringFailureCode.RoundOutOfRange);
    }

    [Test]
    public async Task AwdEvaluator_AllowsSameAttackDimensionsInNextRound()
    {
        var start = DateTimeOffset.UtcNow;
        var attacker = Guid.NewGuid();
        var target = Guid.NewGuid();
        var service = Guid.NewGuid();
        var prior = Attack(target, start.AddSeconds(30), attacker, service);
        prior.Id = Guid.NewGuid();
        var current = Attack(target, start.AddSeconds(70), attacker, service);
        var priorEvent = new ScoringEvent { SubmissionId = prior.Id, Result = ScoringResult.Correct };
        var flag = RoundFlag(target, start.AddSeconds(60), "FLAG{round-1}");

        var result = new AwdSubmissionEvaluator().Evaluate(new(
            current, [priorEvent], [flag], null, AwdJson, "{}", [prior], start));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }

    private static Submission Attack(
        Guid target,
        DateTimeOffset receivedAt,
        Guid? attacker = null,
        Guid? service = null) => new()
    {
        TeamId = attacker ?? Guid.NewGuid(),
        SubjectTeamId = target,
        VictimTeamId = target,
        ServiceId = service ?? Guid.NewGuid(),
        Kind = SubmissionKind.Flag,
        FlagHash = FlagFingerprint.Create("FLAG{round-1}").Sha256,
        FlagLength = "FLAG{round-1}".Length,
        ReceivedAt = receivedAt
    };

    private static ChallengeFlag RoundFlag(Guid target, DateTimeOffset validStart, string flag) => new()
    {
        TeamId = target,
        Flag = flag,
        ValidStart = validStart
    };

    [Test]
    public async Task SubmissionRoundCalculator_UsesLongIntegerBoundariesWithoutOverflow()
    {
        var start = DateTimeOffset.MinValue;

        await Assert.That(SubmissionRoundCalculator.Calculate(start.AddSeconds(59), start, 60)).IsEqualTo(1L);
        await Assert.That(SubmissionRoundCalculator.Calculate(start.AddSeconds(60), start, 60)).IsEqualTo(2L);
        await Assert.That(SubmissionRoundCalculator.Calculate(DateTimeOffset.MaxValue, start, 1))
            .IsGreaterThan((long)int.MaxValue);
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
            FlagHash = FlagFingerprint.Create("flag").Sha256,
            FlagLength = "flag".Length,
            ChallengeInstanceId = Guid.NewGuid(), ReceivedAt = DateTimeOffset.UtcNow
        };
        var flag = new NoCTF.Domain.Challenges.ChallengeFlag
        {
            TeamId = team, ChallengeId = challenge, StageId = secondStage,
            ChallengeInstanceId = submission.ChallengeInstanceId, Flag = "flag"
        };

        var result = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [new ScoringEvent
            {
                SubmissionId = prior.Id,
                TeamId = team,
                ChallengeId = challenge,
                Result = ScoringResult.Correct
            }], [flag], null, "{}", configuration, [prior]));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }

    [Test]
    public async Task PenetrationEvaluator_DoesNotAcceptAnotherStagesFlag()
    {
        var requestedStage = Guid.NewGuid();
        var otherStage = Guid.NewGuid();
        var team = Guid.NewGuid();
        var challenge = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1, [new(requestedStage, 1, "entry", [], null), new(otherStage, 2, "root", [], null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var submission = new Submission
        {
            TeamId = team, ChallengeId = challenge, StageId = requestedStage,
            Kind = SubmissionKind.Flag,
            FlagHash = FlagFingerprint.Create("same-value").Sha256,
            FlagLength = "same-value".Length,
            ChallengeInstanceId = Guid.NewGuid(), ReceivedAt = DateTimeOffset.UtcNow
        };
        var flag = new NoCTF.Domain.Challenges.ChallengeFlag
        {
            TeamId = team, ChallengeId = challenge, StageId = otherStage,
            ChallengeInstanceId = submission.ChallengeInstanceId, Flag = "same-value"
        };

        var result = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [], [flag], null, "{}", configuration));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Wrong);
    }

    [Test]
    public async Task PenetrationEvaluator_AcceptsStaticStageFlagAcrossInstances()
    {
        var stage = Guid.NewGuid();
        var team = Guid.NewGuid();
        var configuration = JsonSerializer.Serialize(new PenetrationChallengeConfiguration(
            1, [new(stage, 1, "entry", [], null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var fingerprint = FlagFingerprint.Create("static-stage-flag");
        var submission = new Submission
        {
            TeamId = team,
            ChallengeId = Guid.NewGuid(),
            StageId = stage,
            ChallengeInstanceId = Guid.NewGuid(),
            Kind = SubmissionKind.Flag,
            FlagHash = fingerprint.Sha256,
            FlagLength = fingerprint.Length,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var flag = new NoCTF.Domain.Challenges.ChallengeFlag
        {
            TeamId = team,
            ChallengeId = submission.ChallengeId!.Value,
            StageId = stage,
            ChallengeInstanceId = null,
            Flag = "static-stage-flag"
        };

        var result = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
            .Evaluate(new(submission, [], [flag], null, "{}", configuration));

        await Assert.That(result.Result).IsEqualTo(ScoringResult.Correct);
    }
}
