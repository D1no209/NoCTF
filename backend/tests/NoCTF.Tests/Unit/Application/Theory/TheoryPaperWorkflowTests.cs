using System.Text.Json;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Tests.Unit.Application.Theory;

public sealed class TheoryPaperWorkflowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TheoryDeadline Deadline = new(Start.AddMinutes(30), TimeSpan.Zero, false, false);

    [Test]
    public async Task Saving_answers_never_grades_and_a_changed_answer_invalidates_old_confirmation()
    {
        var (paper, question) = Paper();
        var oldPaper = paper.ConcurrencyStamp;
        var item = paper.Items.Single();
        var oldItem = item.ConcurrencyStamp;
        var save = new TheorySaveAnswer(item.Id, oldItem, [question.Options[0].Id], Guid.NewGuid(), Start.AddMinutes(1));
        await Assert.That(TheoryPaperWorkflow.SaveAnswer(paper, question, Deadline, save)).IsNull();
        await Assert.That(item.Result).IsNull();
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(0L);
        await Assert.That(TheoryPaperWorkflow.SaveAnswer(paper, question, Deadline, save)).IsEqualTo(TheoryPaperFailure.AnswerConflict);
        await Assert.That(TheoryPaperWorkflow.Submit(paper, new Dictionary<Guid, TheoryQuestionRevision> { [question.Id] = question },
            Deadline, new(oldPaper, save.ActorId, Start.AddMinutes(2)))).IsEqualTo(TheoryPaperFailure.PaperConflict);
        await Assert.That(paper.State).IsEqualTo(TheoryPaperState.Active);
    }

    [Test]
    public async Task Submit_locks_the_whole_paper_and_duplicate_submission_retains_first_authoritative_time()
    {
        var (paper, question) = Paper();
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        TheoryPaperWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [question.Options[0].Id], actor, Start.AddMinutes(1)));
        var versions = new Dictionary<Guid, TheoryQuestionRevision> { [question.Id] = question };
        var submit = new TheorySubmitPaper(paper.ConcurrencyStamp, actor, Start.AddMinutes(2));
        await Assert.That(TheoryPaperWorkflow.Submit(paper, versions, Deadline, submit)).IsNull();
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(10L);
        await Assert.That(TheoryPaperWorkflow.Submit(paper, versions, Deadline, submit with { At = Start.AddHours(2) })).IsNull();
        await Assert.That(paper.SubmittedAt).IsEqualTo(submit.At);
        await Assert.That(TheoryPaperWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [], actor, Start.AddMinutes(3)))).IsEqualTo(TheoryPaperFailure.AlreadySubmitted);
        await Assert.That(TheoryPaperWorkflow.Expire(paper, versions, Deadline, Start.AddHours(3), false)).IsFalse();
    }

    [Test]
    public async Task Deadline_is_exclusive_and_late_timeout_execution_uses_the_due_instant()
    {
        var (paper, question) = Paper();
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        await Assert.That(TheoryPaperWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [question.Options[0].Id], actor, Deadline.At))).IsEqualTo(TheoryPaperFailure.DeadlineReached);
        var versions = new Dictionary<Guid, TheoryQuestionRevision> { [question.Id] = question };
        await Assert.That(TheoryPaperWorkflow.Expire(paper, versions, Deadline, Deadline.At.AddHours(2), false)).IsTrue();
        await Assert.That(paper.SubmittedAt).IsEqualTo(Deadline.At);
        await Assert.That(paper.SubmittedById).IsNull();
        await Assert.That(item.Result).IsEqualTo(TheoryAnswerResult.Unanswered);
        await Assert.That(TheoryPaperGrading.Points(paper)).IsEqualTo(0L);
    }

    [Test]
    public async Task Paused_papers_reject_manual_writes_but_hard_end_can_force_submission()
    {
        var (paper, question) = Paper();
        var paused = Deadline with { Paused = true };
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        await Assert.That(TheoryPaperWorkflow.SaveAnswer(paper, question, paused,
            new(item.Id, item.ConcurrencyStamp, [], actor, Start.AddMinutes(1)))).IsEqualTo(TheoryPaperFailure.Paused);
        var versions = new Dictionary<Guid, TheoryQuestionRevision> { [question.Id] = question };
        await Assert.That(TheoryPaperWorkflow.Submit(paper, versions, paused,
            new(paper.ConcurrencyStamp, actor, Start.AddMinutes(1)))).IsEqualTo(TheoryPaperFailure.Paused);
        await Assert.That(TheoryPaperWorkflow.Expire(paper, versions, paused, Deadline.At, true)).IsTrue();
        await Assert.That(paper.SubmissionKind).IsEqualTo(TheorySubmissionKind.ActivityFinished);
    }

    [Test]
    public async Task Participant_projection_never_contains_answer_keys_and_after_submission_only_returns_score()
    {
        var (paper, question) = Paper();
        question.Explanation = "PRIVATE EXPLANATION";
        var versions = new Dictionary<Guid, TheoryQuestionRevision> { [question.Id] = question };
        var before = JsonSerializer.Serialize(ReadTheoryPaper.Project(paper, versions));
        await Assert.That(before.Contains("PRIVATE EXPLANATION")).IsFalse();
        await Assert.That(before.Contains("IsCorrect")).IsFalse();
        await Assert.That(before.Contains("Result")).IsFalse();
        TheoryPaperWorkflow.Expire(paper, versions, Deadline, Deadline.At, false);
        var after = ReadTheoryPaper.Project(paper, versions);
        await Assert.That(after.Score).IsEqualTo(0L);
        await Assert.That(after.Questions).IsEmpty();
        await Assert.That(JsonSerializer.Serialize(after).Contains(question.Body)).IsFalse();
    }

    [Test]
    public async Task Missing_grading_version_does_not_partially_submit_the_paper()
    {
        var (paper, _) = Paper();
        await Assert.That(() => TheoryPaperWorkflow.Expire(paper, new Dictionary<Guid, TheoryQuestionRevision>(), Deadline,
            Deadline.At, false)).Throws<InvalidOperationException>();
        await Assert.That(paper.State).IsEqualTo(TheoryPaperState.Active);
        await Assert.That(paper.Items.Single().Result).IsNull();
    }

    private static (TheoryPaper Paper, TheoryQuestionRevision Question) Paper()
    {
        var question = TheoryPolicyTests.Question(TheoryQuestionKind.SingleChoice, 1);
        return (new TheoryPaper { Id = Guid.NewGuid(), Attempt = 1, OpenedAt = Start,
            Items = [new() { Id = Guid.NewGuid(), RevisionId = question.Id, Points = 10,
                Options = question.Options.Select((option, index) => new TheoryPaperOption
                    { OptionId = option.Id, Order = question.Options.Count - index - 1 }).ToList() }] }, question);
    }
}
