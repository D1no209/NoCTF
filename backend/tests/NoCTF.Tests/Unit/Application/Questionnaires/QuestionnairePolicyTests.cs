using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Tests.Unit.Application.Questionnaires;

public sealed class QuestionnairePolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(QuestionnaireQuestionKind.SingleChoice, 1, true)]
    [Arguments(QuestionnaireQuestionKind.SingleChoice, 2, false)]
    [Arguments(QuestionnaireQuestionKind.MultipleChoice, 1, false)]
    [Arguments(QuestionnaireQuestionKind.MultipleChoice, 2, true)]
    [Arguments(QuestionnaireQuestionKind.MultipleChoice, 4, true)]
    public async Task Question_requires_the_correct_number_of_correct_options(QuestionnaireQuestionKind kind, int correct, bool valid)
    {
        var revision = Question(kind, correct);
        await Assert.That(ValidateQuestion(revision).Count == 0).IsEqualTo(valid);
    }

    [Test]
    public async Task True_false_is_validated_by_semantics_not_display_order()
    {
        var question = Question(QuestionnaireQuestionKind.TrueFalse, 1, 2);
        question.Options[0].TruthValue = false;
        question.Options[1].TruthValue = true;
        await Assert.That(ValidateQuestion(question)).IsEmpty();
        question.Options[1].TruthValue = false;
        await Assert.That(ValidateQuestion(question).Single().Code).IsEqualTo(CapabilityConfigurationFailure.InvalidOptions);
    }

    [Test]
    public async Task Case_equivalent_tags_and_duplicate_options_are_rejected()
    {
        var revision = Question(QuestionnaireQuestionKind.SingleChoice, 1);
        revision.Classifications = [new() { Kind = QuestionnaireClassificationKind.Tag, Name = "Web", NormalizedName = "WEB" },
            new() { Kind = QuestionnaireClassificationKind.Tag, Name = "web", NormalizedName = "WEB" }];
        await Assert.That(ValidateQuestion(revision).Single().Code).IsEqualTo(CapabilityConfigurationFailure.DuplicateIdentity);
        revision.Classifications.Clear();
        revision.Options[1].Id = revision.Options[0].Id;
        await Assert.That(ValidateQuestion(revision).Single().Code).IsEqualTo(CapabilityConfigurationFailure.InvalidOptions);
    }

    [Test]
    [Arguments(QuestionnaireActivityKind.Examination)]
    [Arguments(QuestionnaireActivityKind.Practice)]
    public async Task Independent_examinations_and_practice_use_the_activity_rules(QuestionnaireActivityKind kind)
        => await Assert.That(QuestionnaireActivityPolicy.Validate(Activity(kind))).IsNull();

    [Test]
    public async Task Unlimited_attempts_are_only_allowed_for_non_cumulative_practice()
    {
        var activity = Activity(QuestionnaireActivityKind.Practice);
        activity.AttemptLimit = null;
        await Assert.That(QuestionnaireActivityPolicy.Validate(activity)).IsNull();
        activity.ScoringMode = QuestionnaireScoringMode.Cumulative;
        await Assert.That(QuestionnaireActivityPolicy.Validate(activity)).IsEqualTo(QuestionnaireActivityFailure.InvalidAttemptLimit);
        activity.ScoringMode = QuestionnaireScoringMode.Highest;
        activity.Kind = QuestionnaireActivityKind.Examination;
        await Assert.That(QuestionnaireActivityPolicy.Validate(activity)).IsEqualTo(QuestionnaireActivityFailure.InvalidAttemptLimit);
    }

    [Test]
    public async Task Every_option_permutation_grades_using_option_identity()
    {
        var revision = Question(QuestionnaireQuestionKind.MultipleChoice, 2);
        var correct = revision.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToArray();
        foreach (var permutation in Permutations(revision.Options.ToArray()))
        {
            revision.Options = permutation.ToList();
            await Assert.That(Evaluate(revision, correct).Value!.Questions.Single().Result).IsEqualTo(QuestionnaireAnswerResult.Correct);
            await Assert.That(Evaluate(revision, [correct[0]]).Value!.Questions.Single().Result).IsEqualTo(QuestionnaireAnswerResult.Wrong);
            await Assert.That(Evaluate(revision, permutation.Select(option => option.Id).ToArray()).Value!.Questions.Single().Result).IsEqualTo(QuestionnaireAnswerResult.Wrong);
        }
        await Assert.That(Evaluate(revision, []).Value!.Questions.Single().Result).IsEqualTo(QuestionnaireAnswerResult.Unanswered);
        await Assert.That(Evaluate(revision, [Guid.NewGuid()]).FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidInput);
        await Assert.That(Evaluate(revision, [correct[0], correct[0]]).FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidInput);
    }

    [Test]
    [Arguments(QuestionnaireScoringMode.Highest, 80L)]
    [Arguments(QuestionnaireScoringMode.Latest, 50L)]
    [Arguments(QuestionnaireScoringMode.Cumulative, 190L)]
    public async Task Scoring_strategies_produce_distinct_totals(QuestionnaireScoringMode mode, long expected)
    {
        QuestionnaireSessionScore[] papers = [new(Guid.NewGuid(), 3, 50, Start.AddMinutes(3)),
            new(Guid.NewGuid(), 1, 60, Start.AddMinutes(1)), new(Guid.NewGuid(), 2, 80, Start.AddMinutes(2))];
        await Assert.That(QuestionnaireSessionGrading.Aggregate(mode, papers).Points).IsEqualTo(expected);
    }

    [Test]
    public async Task Highest_tie_uses_earliest_submission_and_latest_uses_attempt_not_processing_order()
    {
        QuestionnaireSessionScore[] papers = [new(Guid.NewGuid(), 1, 80, Start.AddMinutes(5)), new(Guid.NewGuid(), 2, 80, Start.AddMinutes(2))];
        await Assert.That(QuestionnaireSessionGrading.Aggregate(QuestionnaireScoringMode.Highest, papers).AchievedAt).IsEqualTo(Start.AddMinutes(2));
        await Assert.That(QuestionnaireSessionGrading.Aggregate(QuestionnaireScoringMode.Latest, papers).AchievedAt).IsEqualTo(Start.AddMinutes(2));
        await Assert.That(QuestionnaireSessionGrading.Aggregate(QuestionnaireScoringMode.Highest, []).HasSubmission).IsFalse();
        await Assert.That(QuestionnaireSessionGrading.Aggregate(QuestionnaireScoringMode.Highest, [new(Guid.NewGuid(), 1, 0, Start)]).HasSubmission).IsTrue();
    }

    [Test]
    public async Task Only_fully_graded_submitted_papers_have_points()
    {
        var paper = new QuestionnaireSession { Items = [new() { Points = 10, Result = QuestionnaireAnswerResult.Correct }] };
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(0L);
        paper.State = QuestionnaireSessionState.Submitted;
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(10L);
        paper.Items.Add(new() { Points = 20, Result = QuestionnaireAnswerResult.FullCredit });
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(30L);
        paper.Items.Add(new() { Points = 30 });
        await Assert.That(() => QuestionnaireSessionGrading.Points(paper)).Throws<InvalidOperationException>();
        paper.State = QuestionnaireSessionState.Voided;
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(0L);
    }

    [Test]
    public async Task Overlapping_pause_sources_are_merged_once()
    {
        QuestionnairePauseInterval[] pauses = [new(Start.AddMinutes(5), Start.AddMinutes(15)), new(Start.AddMinutes(10), Start.AddMinutes(20))];
        var deadline = QuestionnaireClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(40));
        await Assert.That(deadline.At).IsEqualTo(Start.AddMinutes(45));
        await Assert.That(deadline.Extension).IsEqualTo(TimeSpan.FromMinutes(15));
        await Assert.That(deadline.Paused).IsFalse();
    }

    [Test]
    public async Task Ongoing_pause_freezes_remaining_time_but_competition_end_still_closes_the_paper()
    {
        QuestionnairePauseInterval[] pauses = [new(Start.AddMinutes(10), null)];
        var first = QuestionnaireClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(15));
        var later = QuestionnaireClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(25));
        await Assert.That(first.At - Start.AddMinutes(15)).IsEqualTo(later.At - Start.AddMinutes(25));
        await Assert.That(later.Paused).IsTrue();
        var capped = QuestionnaireClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(50), Start.AddMinutes(40));
        await Assert.That(capped.At).IsEqualTo(Start.AddMinutes(40));
        await Assert.That(capped.Capped).IsTrue();
        await Assert.That(capped.Paused).IsFalse();
    }

    [Test]
    public async Task Pauses_after_expiration_never_resurrect_a_paper()
    {
        QuestionnairePauseInterval[] pauses = [new(Start.AddMinutes(30), null)];
        var deadline = QuestionnaireClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddHours(1));
        await Assert.That(deadline.At).IsEqualTo(Start.AddMinutes(30));
        await Assert.That(deadline.Paused).IsFalse();
    }

    [Test]
    public async Task Paper_does_not_receive_pause_time_from_before_its_opening()
    {
        QuestionnairePauseInterval[] pauses = [new(Start, Start.AddMinutes(10)), new(Start.AddMinutes(30), Start.AddMinutes(35))];
        var activity = QuestionnaireClock.Calculate(Start, Start.AddHours(2), pauses, Start.AddMinutes(40));
        var paper = QuestionnaireClock.Session(Start.AddMinutes(20), 1800, activity, pauses, Start.AddMinutes(40));
        await Assert.That(paper.At).IsEqualTo(Start.AddMinutes(55));
    }

    internal static QuestionnaireQuestion Question(QuestionnaireQuestionKind kind, int correct, int count = 4)
    {
        var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), ChallengeId = Guid.NewGuid(), Number = 1, CreatedAt = Start };
        var question = new QuestionnaireQuestion
        {
            Id = Guid.NewGuid(), RevisionId = revision.Id, Revision = revision, SourceId = Guid.NewGuid(),
            Title = "Questionnaire question", Body = "Choose the applicable options.", Kind = kind,
            Options = Enumerable.Range(0, count).Select(index => new QuestionnaireOption
            { Id = Guid.NewGuid(), Position = index, Content = $"Option {index}", IsCorrect = index < correct }).ToList()
        };
        revision.Questions.Add(question);
        return question;
    }

    private static IReadOnlyList<CapabilityConfigurationError> ValidateQuestion(QuestionnaireQuestion question) =>
        ChallengeCapabilityValidation.Validate(new ChallengeDefinition { Capabilities =
            [new QuestionnaireCapabilityDefinition { CurrentRevisionId = question.RevisionId, CurrentRevision = question.Revision }] });

    private static NoCTF.Application.Common.OperationResult<QuestionnaireEvaluation, CapabilityExecutionFailure> Evaluate(
        QuestionnaireQuestion question, IReadOnlyList<Guid> selected) => new EvaluateQuestionnaire().Execute(new(Guid.NewGuid(),
            question.Revision, [question.Id], [new(question.Id, selected)], Start));

    private static QuestionnaireActivity Activity(QuestionnaireActivityKind kind) => new()
    {
        Name = "Synthetic activity", Kind = kind, OpensAt = Start, ClosesAt = Start.AddHours(1), DurationSeconds = 1800,
        Rules = [new() { Id = Guid.NewGuid(), Count = 1, Points = 10 }]
    };

    private static IEnumerable<T[]> Permutations<T>(T[] items) => items.Length == 0 ? [Array.Empty<T>()]
        : items.SelectMany((item, index) => Permutations(items.Where((_, other) => other != index).ToArray())
            .Select(tail => new[] { item }.Concat(tail).ToArray()));
}
