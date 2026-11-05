using System.Text.Json;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.Application.Questionnaires;

public sealed class QuestionnaireSessionWorkflowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly QuestionnaireDeadline Deadline = new(Start.AddMinutes(30), TimeSpan.Zero, false, false);

    [Test]
    public async Task Saving_answers_never_grades_and_a_changed_answer_invalidates_old_confirmation()
    {
        var (paper, question) = Paper();
        var oldPaper = paper.ConcurrencyStamp;
        var item = paper.Items.Single();
        var oldItem = item.ConcurrencyStamp;
        var save = new SaveQuestionnaireAnswer(item.Id, oldItem, [question.Options[0].Id], Guid.NewGuid(), Start.AddMinutes(1));
        await Assert.That(QuestionnaireSessionWorkflow.SaveAnswer(paper, question, Deadline, save)).IsNull();
        await Assert.That(item.Result).IsNull();
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(0L);
        await Assert.That(QuestionnaireSessionWorkflow.SaveAnswer(paper, question, Deadline, save)).IsEqualTo(QuestionnaireSessionFailure.AnswerConflict);
        await Assert.That(QuestionnaireSessionWorkflow.Submit(paper, new Dictionary<Guid, QuestionnaireQuestion> { [question.VersionId] = question },
            Deadline, new(oldPaper, save.ActorId, Start.AddMinutes(2)))).IsEqualTo(QuestionnaireSessionFailure.SessionConflict);
        await Assert.That(paper.State).IsEqualTo(QuestionnaireSessionState.Active);
    }

    [Test]
    public async Task Submit_locks_the_whole_paper_and_duplicate_submission_retains_first_authoritative_time()
    {
        var (paper, question) = Paper();
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        QuestionnaireSessionWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [question.Options[0].Id], actor, Start.AddMinutes(1)));
        var versions = new Dictionary<Guid, QuestionnaireQuestion> { [question.VersionId] = question };
        var submit = new SubmitQuestionnaireSession(paper.ConcurrencyStamp, actor, Start.AddMinutes(2));
        await Assert.That(QuestionnaireSessionWorkflow.Submit(paper, versions, Deadline, submit)).IsNull();
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(10L);
        await Assert.That(QuestionnaireSessionWorkflow.Submit(paper, versions, Deadline, submit with { At = Start.AddHours(2) })).IsNull();
        await Assert.That(paper.SubmittedAt).IsEqualTo(submit.At);
        await Assert.That(QuestionnaireSessionWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [], actor, Start.AddMinutes(3)))).IsEqualTo(QuestionnaireSessionFailure.AlreadySubmitted);
        await Assert.That(QuestionnaireSessionWorkflow.Expire(paper, versions, Deadline, Start.AddHours(3), false)).IsFalse();
    }

    [Test]
    public async Task Deadline_is_exclusive_and_late_timeout_execution_uses_the_due_instant()
    {
        var (paper, question) = Paper();
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        await Assert.That(QuestionnaireSessionWorkflow.SaveAnswer(paper, question, Deadline,
            new(item.Id, item.ConcurrencyStamp, [question.Options[0].Id], actor, Deadline.At))).IsEqualTo(QuestionnaireSessionFailure.DeadlineReached);
        var versions = new Dictionary<Guid, QuestionnaireQuestion> { [question.VersionId] = question };
        await Assert.That(QuestionnaireSessionWorkflow.Expire(paper, versions, Deadline, Deadline.At.AddHours(2), false)).IsTrue();
        await Assert.That(paper.SubmittedAt).IsEqualTo(Deadline.At);
        await Assert.That(paper.SubmittedById).IsNull();
        await Assert.That(item.Result).IsEqualTo(QuestionnaireAnswerResult.Unanswered);
        await Assert.That(QuestionnaireSessionGrading.Points(paper)).IsEqualTo(0L);
    }

    [Test]
    public async Task Paused_papers_reject_manual_writes_but_hard_end_can_force_submission()
    {
        var (paper, question) = Paper();
        var paused = Deadline with { Paused = true };
        var item = paper.Items.Single();
        var actor = Guid.NewGuid();
        await Assert.That(QuestionnaireSessionWorkflow.SaveAnswer(paper, question, paused,
            new(item.Id, item.ConcurrencyStamp, [], actor, Start.AddMinutes(1)))).IsEqualTo(QuestionnaireSessionFailure.Paused);
        var versions = new Dictionary<Guid, QuestionnaireQuestion> { [question.VersionId] = question };
        await Assert.That(QuestionnaireSessionWorkflow.Submit(paper, versions, paused,
            new(paper.ConcurrencyStamp, actor, Start.AddMinutes(1)))).IsEqualTo(QuestionnaireSessionFailure.Paused);
        await Assert.That(QuestionnaireSessionWorkflow.Expire(paper, versions, paused, Deadline.At, true)).IsTrue();
        await Assert.That(paper.SubmissionKind).IsEqualTo(QuestionnaireSubmissionKind.ActivityFinished);
    }

    [Test]
    public async Task Participant_projection_never_contains_answer_keys_and_after_submission_only_returns_score()
    {
        var (paper, question) = Paper();
        question.Explanation = "PRIVATE EXPLANATION";
        var versions = new Dictionary<Guid, QuestionnaireQuestion> { [question.VersionId] = question };
        var before = JsonSerializer.Serialize(ReadQuestionnaireSession.Project(paper, versions));
        await Assert.That(before.Contains("PRIVATE EXPLANATION")).IsFalse();
        await Assert.That(before.Contains("IsCorrect")).IsFalse();
        await Assert.That(before.Contains("Result")).IsFalse();
        QuestionnaireSessionWorkflow.Expire(paper, versions, Deadline, Deadline.At, false);
        var after = ReadQuestionnaireSession.Project(paper, versions);
        await Assert.That(after.Score).IsEqualTo(0L);
        await Assert.That(after.Questions).IsEmpty();
        await Assert.That(JsonSerializer.Serialize(after).Contains(question.Body)).IsFalse();
    }

    [Test]
    public async Task Missing_grading_version_does_not_partially_submit_the_paper()
    {
        var (paper, _) = Paper();
        await Assert.That(() => QuestionnaireSessionWorkflow.Expire(paper, new Dictionary<Guid, QuestionnaireQuestion>(), Deadline,
            Deadline.At, false)).Throws<InvalidOperationException>();
        await Assert.That(paper.State).IsEqualTo(QuestionnaireSessionState.Active);
        await Assert.That(paper.Items.Single().Result).IsNull();
    }

    private static (QuestionnaireSession Paper, QuestionnaireQuestion Question) Paper()
    {
        var question = QuestionnairePolicyTests.Question(QuestionnaireQuestionKind.SingleChoice, 1);
        var assignment = new NoCTF.Domain.Challenges.Capabilities.QuestionnaireAssignment
        {
            Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), ChallengeId = Guid.NewGuid(), RevisionId = Guid.NewGuid()
        };
        question.Revision.ChallengeId = assignment.ChallengeId;
        question.Revision.Id = assignment.RevisionId;
        question.RevisionId = assignment.RevisionId;
        return (new QuestionnaireSession { Id = assignment.SessionId, Attempt = 1, OpenedAt = Start, Assignments = [assignment],
            Items = [new() { Id = Guid.NewGuid(), RevisionId = question.VersionId, Points = 10, AssignmentId = assignment.Id,
                QuestionnaireRevisionId = assignment.RevisionId, QuestionnaireQuestionId = question.Id,
                Options = question.Options.Select((option, index) => new QuestionnaireSessionOption
                    { OptionId = option.Id, Order = question.Options.Count - index - 1 }).ToList() }] }, question);
    }
}
