using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Questionnaires.Sessions;

public enum QuestionnaireSessionFailure
{
    NotFoundOrForbidden, NotOpen, Paused, DeadlineReached, AlreadySubmitted, Voided,
    AnswerConflict, SessionConflict, InvalidOptions, InvalidRequestIdentity, AttemptsExhausted,
    InsufficientQuestions, IssuanceStopped, DependencyUnavailable
}

public sealed record SaveQuestionnaireAnswer(Guid ItemId, Guid ExpectedRevision, IReadOnlyList<Guid> OptionIds,
    Guid ActorId, DateTimeOffset At);
public sealed record SubmitQuestionnaireSession(Guid ExpectedRevision, Guid ActorId, DateTimeOffset At);

public static class QuestionnaireSessionWorkflow
{
    public static QuestionnaireSessionFailure? SaveAnswer(QuestionnaireSession session, QuestionnaireQuestion question,
        QuestionnaireDeadline deadline, SaveQuestionnaireAnswer command)
    {
        if (session.State == QuestionnaireSessionState.Voided) return QuestionnaireSessionFailure.Voided;
        if (session.State != QuestionnaireSessionState.Active) return QuestionnaireSessionFailure.AlreadySubmitted;
        if (command.At >= deadline.At) return QuestionnaireSessionFailure.DeadlineReached;
        if (deadline.Paused) return QuestionnaireSessionFailure.Paused;
        var item = session.Items.SingleOrDefault(item => item.Id == command.ItemId);
        if (item is null || item.RevisionId != question.VersionId) return QuestionnaireSessionFailure.NotFoundOrForbidden;
        if (item.ConcurrencyStamp != command.ExpectedRevision) return QuestionnaireSessionFailure.AnswerConflict;
        var selection = command.OptionIds.ToHashSet();
        if (selection.Count != command.OptionIds.Count
            || selection.Any(id => item.Options.All(option => option.OptionId != id))
            || selection.Any(id => question.Options.All(option => option.Id != id))
            || question.Kind != QuestionnaireQuestionKind.MultipleChoice && selection.Count > 1)
            return QuestionnaireSessionFailure.InvalidOptions;
        if (command.ActorId == Guid.Empty) return QuestionnaireSessionFailure.NotFoundOrForbidden;
        foreach (var option in item.Options) option.Selected = selection.Contains(option.OptionId);
        item.AnsweredById = command.ActorId; item.AnsweredAt = command.At;
        item.ConcurrencyStamp = Guid.NewGuid(); session.ConcurrencyStamp = Guid.NewGuid();
        return null;
    }

    public static QuestionnaireSessionFailure? Submit(QuestionnaireSession session, IReadOnlyDictionary<Guid, QuestionnaireQuestion> questions,
        QuestionnaireDeadline deadline, SubmitQuestionnaireSession command)
    {
        if (session.State == QuestionnaireSessionState.Voided) return QuestionnaireSessionFailure.Voided;
        if (session.State == QuestionnaireSessionState.Submitted) return null;
        if (command.At >= deadline.At) return QuestionnaireSessionFailure.DeadlineReached;
        if (deadline.Paused) return QuestionnaireSessionFailure.Paused;
        if (session.ConcurrencyStamp != command.ExpectedRevision) return QuestionnaireSessionFailure.SessionConflict;
        if (command.ActorId == Guid.Empty) return QuestionnaireSessionFailure.NotFoundOrForbidden;
        Grade(session, questions, command.At, QuestionnaireSubmissionKind.Manual, command.ActorId);
        return null;
    }

    public static bool Expire(QuestionnaireSession session, IReadOnlyDictionary<Guid, QuestionnaireQuestion> questions,
        QuestionnaireDeadline deadline, DateTimeOffset now, bool activityFinished)
    {
        if (session.State != QuestionnaireSessionState.Active || now < deadline.At) return false;
        Grade(session, questions, deadline.At,
            activityFinished ? QuestionnaireSubmissionKind.ActivityFinished : QuestionnaireSubmissionKind.Timeout, null);
        return true;
    }

    private static void Grade(QuestionnaireSession session, IReadOnlyDictionary<Guid, QuestionnaireQuestion> questions,
        DateTimeOffset submittedAt, QuestionnaireSubmissionKind kind, Guid? actorId)
    {
        if (session.Items.Count == 0 || session.Items.Any(item => !questions.ContainsKey(item.RevisionId)))
            throw new InvalidOperationException("A session can only be graded against all of its assigned versions.");
        if (session.Assignments.Count == 0 || session.Items.Any(item => session.Assignments.All(assignment => assignment.Id != item.AssignmentId)))
            throw new InvalidOperationException("Every question must belong to an assigned questionnaire.");
        var evaluated = session.Assignments.Select(assignment =>
        {
            var items = session.Items.Where(item => item.AssignmentId == assignment.Id).ToArray();
            var fact = new QuestionnaireAttemptGameplayFact
            {
                Id = Guid.CreateVersion7(submittedAt), QuestionnaireAssignmentId = assignment.Id, ActorUserId = actorId,
                OccurredAt = submittedAt, UpdatedAt = submittedAt, State = GameplayFactState.Completed, Result = GameplayFactResult.Applied
            };
            var revision = new QuestionnaireRevision
            {
                Id = assignment.RevisionId, ChallengeId = assignment.ChallengeId,
                Questions = items.Select(item =>
                {
                    var question = questions[item.RevisionId];
                    if (question.RevisionId != assignment.RevisionId || question.Revision.ChallengeId != assignment.ChallengeId
                        || question.Id != item.QuestionnaireQuestionId)
                        throw new InvalidOperationException("The issued question is outside its assigned questionnaire.");
                    return question;
                }).ToList()
            };
            var outcome = new EvaluateQuestionnaire().Execute(new(fact.Id, revision,
                items.Select(item => item.QuestionnaireQuestionId).ToArray(), items.Select(item => new QuestionnaireAnswer(item.QuestionnaireQuestionId,
                    item.Options.Where(option => option.Selected).Select(option => option.OptionId).ToArray())).ToArray(), submittedAt));
            if (!outcome.Succeeded) throw new InvalidOperationException("Stored answers are invalid for their assigned questionnaire.");
            fact.CapabilityOutcomes.Add(outcome.Value!.ToGameplayOutcome());
            return (assignment, fact);
        }).ToArray();
        foreach (var result in evaluated) result.assignment.Fact = result.fact;
        session.State = QuestionnaireSessionState.Submitted; session.SubmittedAt = submittedAt;
        session.SubmissionKind = kind; session.SubmittedById = actorId; session.ConcurrencyStamp = Guid.NewGuid();
        QuestionnaireFactResults.Read(session);
    }
}
