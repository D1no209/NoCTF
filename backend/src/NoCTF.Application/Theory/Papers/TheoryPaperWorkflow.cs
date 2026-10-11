using NoCTF.Application.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Papers;

public enum TheoryPaperFailure
{
    NotFoundOrForbidden, NotOpen, Paused, DeadlineReached, AlreadySubmitted, Voided,
    AnswerConflict, PaperConflict, InvalidOptions, InvalidRequestIdentity, AttemptsExhausted,
    InsufficientQuestions, IssuanceStopped, DependencyUnavailable
}

public sealed record TheorySaveAnswer(Guid ItemId, Guid ExpectedRevision, IReadOnlyList<Guid> OptionIds,
    Guid ActorId, DateTimeOffset At);
public sealed record TheorySubmitPaper(Guid ExpectedRevision, Guid ActorId, DateTimeOffset At);

/// <summary>
/// Mutates the aggregate inside its caller's serializable transaction. No outcome may be returned
/// to a participant before that transaction commits. Identity/access checks are required before use.
/// </summary>
public static class TheoryPaperWorkflow
{
    public static TheoryPaperFailure? SaveAnswer(TheoryPaper paper, TheoryQuestionRevision question,
        TheoryDeadline deadline, TheorySaveAnswer command)
    {
        if (paper.State == TheoryPaperState.Voided) return TheoryPaperFailure.Voided;
        if (paper.State != TheoryPaperState.Active) return TheoryPaperFailure.AlreadySubmitted;
        if (command.At >= deadline.At) return TheoryPaperFailure.DeadlineReached;
        if (deadline.Paused) return TheoryPaperFailure.Paused;
        var item = paper.Items.SingleOrDefault(item => item.Id == command.ItemId);
        if (item is null || item.RevisionId != question.Id) return TheoryPaperFailure.NotFoundOrForbidden;
        if (item.ConcurrencyStamp != command.ExpectedRevision) return TheoryPaperFailure.AnswerConflict;
        var selection = command.OptionIds.ToHashSet();
        if (selection.Count != command.OptionIds.Count
            || selection.Any(id => item.Options.All(option => option.OptionId != id))
            || selection.Any(id => question.Options.All(option => option.Id != id))
            || question.Kind != TheoryQuestionKind.MultipleChoice && selection.Count > 1)
            return TheoryPaperFailure.InvalidOptions;
        if (command.ActorId == Guid.Empty) return TheoryPaperFailure.NotFoundOrForbidden;
        foreach (var option in item.Options) option.Selected = selection.Contains(option.OptionId);
        item.AnsweredById = command.ActorId;
        item.AnsweredAt = command.At;
        item.ConcurrencyStamp = Guid.NewGuid();
        paper.ConcurrencyStamp = Guid.NewGuid();
        // Deliberately do not evaluate or write a result while the paper is active.
        return null;
    }

    public static TheoryPaperFailure? Submit(TheoryPaper paper, IReadOnlyDictionary<Guid, TheoryQuestionRevision> questions,
        TheoryDeadline deadline, TheorySubmitPaper command)
    {
        if (paper.State == TheoryPaperState.Voided) return TheoryPaperFailure.Voided;
        if (paper.State == TheoryPaperState.Submitted) return null;
        if (command.At >= deadline.At) return TheoryPaperFailure.DeadlineReached;
        if (deadline.Paused) return TheoryPaperFailure.Paused;
        if (paper.ConcurrencyStamp != command.ExpectedRevision) return TheoryPaperFailure.PaperConflict;
        if (command.ActorId == Guid.Empty) return TheoryPaperFailure.NotFoundOrForbidden;
        Grade(paper, questions, command.At, TheorySubmissionKind.Manual, command.ActorId);
        return null;
    }

    public static bool Expire(TheoryPaper paper, IReadOnlyDictionary<Guid, TheoryQuestionRevision> questions,
        TheoryDeadline deadline, DateTimeOffset now, bool activityFinished)
    {
        if (paper.State != TheoryPaperState.Active || now < deadline.At) return false;
        Grade(paper, questions, deadline.At,
            activityFinished ? TheorySubmissionKind.ActivityFinished : TheorySubmissionKind.Timeout, null);
        return true;
    }

    private static void Grade(TheoryPaper paper, IReadOnlyDictionary<Guid, TheoryQuestionRevision> questions,
        DateTimeOffset submittedAt, TheorySubmissionKind kind, Guid? actorId)
    {
        if (paper.Items.Count == 0 || paper.Items.Any(item => !questions.ContainsKey(item.RevisionId)))
            throw new InvalidOperationException("A paper can only be graded against all of its assigned versions.");
        // Calculate every result before changing any state, including when stored selections are invalid.
        var results = paper.Items.Select(item => TheoryPaperGrading.Evaluate(questions[item.RevisionId],
            item.Options.Where(option => option.Selected).Select(option => option.OptionId).ToArray())).ToArray();
        for (var index = 0; index < results.Length; index++) paper.Items[index].Result = results[index];
        paper.State = TheoryPaperState.Submitted;
        paper.SubmittedAt = submittedAt;
        paper.SubmissionKind = kind;
        paper.SubmittedById = actorId;
        paper.ConcurrencyStamp = Guid.NewGuid();
    }
}
