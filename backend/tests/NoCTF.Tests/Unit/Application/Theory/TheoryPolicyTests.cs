using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Tests.Unit.Application.Theory;

public sealed class TheoryPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(TheoryQuestionKind.SingleChoice, 1, true)]
    [Arguments(TheoryQuestionKind.SingleChoice, 2, false)]
    [Arguments(TheoryQuestionKind.MultipleChoice, 1, false)]
    [Arguments(TheoryQuestionKind.MultipleChoice, 2, true)]
    [Arguments(TheoryQuestionKind.MultipleChoice, 4, true)]
    public async Task Question_requires_the_correct_number_of_correct_options(TheoryQuestionKind kind, int correct, bool valid)
    {
        var revision = Question(kind, correct);
        await Assert.That(TheoryQuestionPolicy.Validate(revision) is null).IsEqualTo(valid);
    }

    [Test]
    public async Task True_false_is_validated_by_semantics_not_display_order()
    {
        var question = Question(TheoryQuestionKind.TrueFalse, 1, 2);
        question.Options[0].TruthValue = TheoryTruthValue.False;
        question.Options[1].TruthValue = TheoryTruthValue.True;
        await Assert.That(TheoryQuestionPolicy.Validate(question)).IsNull();
        question.Options[1].TruthValue = TheoryTruthValue.False;
        await Assert.That(TheoryQuestionPolicy.Validate(question)).IsEqualTo(TheoryQuestionFailure.InvalidTruthOptions);
    }

    [Test]
    public async Task Use_permission_never_grants_answer_editing_or_management()
    {
        var actor = Guid.NewGuid();
        var question = new TheoryQuestion { OwnerId = Guid.NewGuid(), Grants = [new() { UserId = actor, Permission = TheoryQuestionPermission.Use }] };
        await Assert.That(TheoryQuestionPolicy.Allows(question, actor, false, TheoryQuestionPermission.Use)).IsTrue();
        await Assert.That(TheoryQuestionPolicy.Allows(question, actor, false, TheoryQuestionPermission.Edit)).IsFalse();
        await Assert.That(TheoryQuestionPolicy.Allows(question, actor, false, TheoryQuestionPermission.Manage)).IsFalse();
    }

    [Test]
    public async Task Case_equivalent_tags_and_duplicate_options_are_rejected()
    {
        var revision = Question(TheoryQuestionKind.SingleChoice, 1);
        revision.Classifications = [new() { Kind = TheoryClassificationKind.Tag, Name = "Web", NormalizedName = "WEB" },
            new() { Kind = TheoryClassificationKind.Tag, Name = "web", NormalizedName = "WEB" }];
        await Assert.That(TheoryQuestionPolicy.Validate(revision)).IsEqualTo(TheoryQuestionFailure.DuplicateClassification);
        revision.Classifications.Clear();
        revision.Options[1].Id = revision.Options[0].Id;
        await Assert.That(TheoryQuestionPolicy.Validate(revision)).IsEqualTo(TheoryQuestionFailure.InvalidOptionIdentity);
    }

    [Test]
    [Arguments(GameMode.Ctf, true)]
    [Arguments(GameMode.Awd, true)]
    [Arguments(GameMode.Awdp, true)]
    [Arguments(GameMode.Koh, false)]
    [Arguments(GameMode.LiveSolo, false)]
    public async Task Only_supported_competition_modes_can_enable_theory(GameMode mode, bool supported)
        => await Assert.That(TheoryActivityPolicy.Validate(Activity(TheoryActivityKind.Competition), mode) is null).IsEqualTo(supported);

    [Test]
    public async Task Unlimited_attempts_are_only_allowed_for_non_cumulative_practice()
    {
        var activity = Activity(TheoryActivityKind.Practice);
        activity.AttemptLimit = null;
        await Assert.That(TheoryActivityPolicy.Validate(activity)).IsNull();
        activity.ScoringMode = TheoryScoringMode.Cumulative;
        await Assert.That(TheoryActivityPolicy.Validate(activity)).IsEqualTo(TheoryActivityFailure.InvalidAttemptLimit);
        activity.ScoringMode = TheoryScoringMode.Highest;
        activity.Kind = TheoryActivityKind.Examination;
        await Assert.That(TheoryActivityPolicy.Validate(activity)).IsEqualTo(TheoryActivityFailure.InvalidAttemptLimit);
    }

    [Test]
    public async Task Every_option_permutation_grades_using_option_identity()
    {
        var revision = Question(TheoryQuestionKind.MultipleChoice, 2);
        var correct = revision.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToArray();
        foreach (var permutation in Permutations(revision.Options.ToArray()))
        {
            revision.Options = permutation.ToList();
            await Assert.That(TheoryPaperGrading.Evaluate(revision, correct)).IsEqualTo(TheoryAnswerResult.Correct);
            await Assert.That(TheoryPaperGrading.Evaluate(revision, [correct[0]])).IsEqualTo(TheoryAnswerResult.Wrong);
            await Assert.That(TheoryPaperGrading.Evaluate(revision, permutation.Select(option => option.Id).ToArray())).IsEqualTo(TheoryAnswerResult.Wrong);
        }
        await Assert.That(TheoryPaperGrading.Evaluate(revision, [])).IsEqualTo(TheoryAnswerResult.Unanswered);
        await Assert.That(() => TheoryPaperGrading.Evaluate(revision, [Guid.NewGuid()])).Throws<ArgumentException>();
        await Assert.That(() => TheoryPaperGrading.Evaluate(revision, [correct[0], correct[0]])).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(TheoryScoringMode.Highest, 80L)]
    [Arguments(TheoryScoringMode.Latest, 50L)]
    [Arguments(TheoryScoringMode.Cumulative, 190L)]
    public async Task Scoring_strategies_produce_distinct_totals(TheoryScoringMode mode, long expected)
    {
        TheoryPaperScore[] papers = [new(Guid.NewGuid(), 3, 50, Start.AddMinutes(3)),
            new(Guid.NewGuid(), 1, 60, Start.AddMinutes(1)), new(Guid.NewGuid(), 2, 80, Start.AddMinutes(2))];
        await Assert.That(TheoryPaperGrading.Aggregate(mode, papers).Points).IsEqualTo(expected);
    }

    [Test]
    public async Task Highest_tie_uses_earliest_submission_and_latest_uses_attempt_not_processing_order()
    {
        TheoryPaperScore[] papers = [new(Guid.NewGuid(), 1, 80, Start.AddMinutes(5)), new(Guid.NewGuid(), 2, 80, Start.AddMinutes(2))];
        await Assert.That(TheoryPaperGrading.Aggregate(TheoryScoringMode.Highest, papers).AchievedAt).IsEqualTo(Start.AddMinutes(2));
        await Assert.That(TheoryPaperGrading.Aggregate(TheoryScoringMode.Latest, papers).AchievedAt).IsEqualTo(Start.AddMinutes(2));
        await Assert.That(TheoryPaperGrading.Aggregate(TheoryScoringMode.Highest, []).HasSubmission).IsFalse();
        await Assert.That(TheoryPaperGrading.Aggregate(TheoryScoringMode.Highest, [new(Guid.NewGuid(), 1, 0, Start)]).HasSubmission).IsTrue();
    }

    [Test]
    public async Task Only_fully_graded_submitted_papers_have_points()
    {
        var paper = new TheoryPaper { Items = [new() { Points = 10, Result = TheoryAnswerResult.Correct }] };
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(0L);
        paper.State = TheoryPaperState.Submitted;
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(10L);
        paper.Items.Add(new() { Points = 20, Result = TheoryAnswerResult.FullCredit });
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(30L);
        paper.Items.Add(new() { Points = 30 });
        await Assert.That(() => TheoryPaperGrading.Points(paper)).Throws<InvalidOperationException>();
        paper.State = TheoryPaperState.Voided;
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(0L);
    }

    [Test]
    public async Task Overlapping_pause_sources_are_merged_once()
    {
        TheoryPauseInterval[] pauses = [new(Start.AddMinutes(5), Start.AddMinutes(15)), new(Start.AddMinutes(10), Start.AddMinutes(20))];
        var deadline = TheoryClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(40));
        await Assert.That(deadline.At).IsEqualTo(Start.AddMinutes(45));
        await Assert.That(deadline.Extension).IsEqualTo(TimeSpan.FromMinutes(15));
        await Assert.That(deadline.Paused).IsFalse();
    }

    [Test]
    public async Task Ongoing_pause_freezes_remaining_time_but_competition_end_still_closes_the_paper()
    {
        TheoryPauseInterval[] pauses = [new(Start.AddMinutes(10), null)];
        var first = TheoryClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(15));
        var later = TheoryClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(25));
        await Assert.That(first.At - Start.AddMinutes(15)).IsEqualTo(later.At - Start.AddMinutes(25));
        await Assert.That(later.Paused).IsTrue();
        var capped = TheoryClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddMinutes(50), Start.AddMinutes(40));
        await Assert.That(capped.At).IsEqualTo(Start.AddMinutes(40));
        await Assert.That(capped.Capped).IsTrue();
        await Assert.That(capped.Paused).IsFalse();
    }

    [Test]
    public async Task Pauses_after_expiration_never_resurrect_a_paper()
    {
        TheoryPauseInterval[] pauses = [new(Start.AddMinutes(30), null)];
        var deadline = TheoryClock.Calculate(Start, Start.AddMinutes(30), pauses, Start.AddHours(1));
        await Assert.That(deadline.At).IsEqualTo(Start.AddMinutes(30));
        await Assert.That(deadline.Paused).IsFalse();
    }

    [Test]
    public async Task Paper_does_not_receive_pause_time_from_before_its_opening()
    {
        TheoryPauseInterval[] pauses = [new(Start, Start.AddMinutes(10)), new(Start.AddMinutes(30), Start.AddMinutes(35))];
        var activity = TheoryClock.Calculate(Start, Start.AddHours(2), pauses, Start.AddMinutes(40));
        var paper = TheoryClock.Paper(Start.AddMinutes(20), 1800, activity, pauses, Start.AddMinutes(40));
        await Assert.That(paper.At).IsEqualTo(Start.AddMinutes(55));
    }

    internal static TheoryQuestionRevision Question(TheoryQuestionKind kind, int correct, int count = 4) => new()
    {
        Id = Guid.NewGuid(), Title = "Synthetic theory question", Body = "Choose the applicable options.", Kind = kind,
        Options = Enumerable.Range(0, count).Select(index => new TheoryQuestionOption
        { Id = Guid.NewGuid(), Order = index, Content = $"Option {index}", IsCorrect = index < correct }).ToList()
    };

    private static TheoryActivity Activity(TheoryActivityKind kind) => new()
    {
        Name = "Synthetic activity", Kind = kind, OpensAt = Start, ClosesAt = Start.AddHours(1), DurationSeconds = 1800,
        Rules = [new() { Id = Guid.NewGuid(), Count = 1, Points = 10 }]
    };

    private static IEnumerable<T[]> Permutations<T>(T[] items) => items.Length == 0 ? [Array.Empty<T>()]
        : items.SelectMany((item, index) => Permutations(items.Where((_, other) => other != index).ToArray())
            .Select(tail => new[] { item }.Concat(tail).ToArray()));
}
