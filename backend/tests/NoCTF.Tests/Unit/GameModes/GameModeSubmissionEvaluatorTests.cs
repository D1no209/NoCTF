using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Submission;

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
}
