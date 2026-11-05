using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Application.Questionnaires;

public static class QuestionnaireFactResults
{
    public static void Read(QuestionnaireSession session)
    {
        if (session.State == QuestionnaireSessionState.Active) return;
        var results = new Dictionary<Guid, QuestionnaireAnswerResult>();
        foreach (var assignment in session.Assignments)
        {
            if (session.State == QuestionnaireSessionState.Voided && assignment.Fact is null) continue;
            var outcome = Outcome(assignment);
            foreach (var item in session.Items.Where(item => item.AssignmentId == assignment.Id))
            {
                var result = outcome.Questions.Single(question => question.QuestionId == item.QuestionnaireQuestionId).Result;
                if (!Enum.IsDefined(result)) throw new InvalidOperationException("Unknown questionnaire result.");
                results.Add(item.Id, result);
            }
        }
        if (session.State == QuestionnaireSessionState.Submitted && results.Count != session.Items.Count)
            throw new InvalidOperationException("A submitted session must have authoritative results for every assigned question.");
        foreach (var item in session.Items) item.Result = results.TryGetValue(item.Id, out var result) ? result : null;
    }

    public static void Write(QuestionnaireSession session, DateTimeOffset now)
    {
        if (session.State == QuestionnaireSessionState.Active) return;
        if (session.State == QuestionnaireSessionState.Voided)
        {
            foreach (var assignment in session.Assignments.Where(assignment => assignment.Fact is not null))
            {
                var fact = assignment.Fact!;
                if (fact.Kind != GameplayFactKind.QuestionnaireAttempt) throw new InvalidOperationException("Invalid assignment fact kind.");
                if (fact.Result == GameplayFactResult.Rejected) continue;
                fact.Result = GameplayFactResult.Rejected; fact.State = GameplayFactState.Completed; fact.UpdatedAt = now;
            }
            return;
        }
        var changes = session.Items.Select(item =>
        {
            var assignment = session.Assignments.Single(assignment => assignment.Id == item.AssignmentId);
            var outcome = Outcome(assignment);
            var result = item.Result ?? throw new InvalidOperationException("A submitted question must be graded before saving its fact.");
            if (!Enum.IsDefined(result)) throw new InvalidOperationException("Unknown questionnaire result.");
            return (assignment, outcome, result, question: outcome.Questions.Single(question => question.QuestionId == item.QuestionnaireQuestionId));
        }).ToArray();
        foreach (var change in changes)
        {
            if (change.question.Result == change.result) continue;
            change.question.Result = change.result;
            change.outcome.ObservedAt = now;
            change.assignment.Fact!.UpdatedAt = now;
        }
    }

    private static QuestionnaireOutcome Outcome(QuestionnaireAssignment assignment)
    {
        if (assignment.Fact is not { Kind: GameplayFactKind.QuestionnaireAttempt, CompetitionId: null, CompetitionChallengeId: null, TeamId: null } fact
            || fact.QuestionnaireAssignmentId != assignment.Id)
            throw new InvalidOperationException("The assignment requires its independent questionnaire fact.");
        var outcome = fact.CapabilityOutcomes.OfType<QuestionnaireOutcome>().Single();
        if (outcome.RevisionId != assignment.RevisionId) throw new InvalidOperationException("The result refers to a different questionnaire revision.");
        return outcome;
    }
}
